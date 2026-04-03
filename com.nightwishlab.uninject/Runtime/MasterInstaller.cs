using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manager Layer 컴포넌트들( [Referral] )을 전역 레지스트리로 관리하고,
/// 런타임에는 Resolve API 를 통해 전역 의존성을 제공하는 싱글톤.
///
/// - 에디터: RefreshRegistry() 로 현재 씬의 매니저 목록을 스캔하여
///           _globalReferrals (직렬화 리스트)에 BakeUp
/// - 런타임: Bake된 _globalReferrals 를 기반으로 딕셔너리를 구성하고 Resolve() 로 조회
///
/// ※ 런타임에서는 MonoBehaviour 전체 순회를 하지 않음.
///
/// [개선 v1.1]
///   - 공통 로직 → InstallerRegistryHelper.
///   - Safety Net armed/disarmed 패턴으로 1회 복구.
///
/// [개선 v2.0]
///   - 레지스트리: Dictionary&lt;Type, Component&gt; → Dictionary&lt;RegistryKey, Component&gt;.
///   - Named/Keyed Resolve: Resolve(Type, string id), ResolveAs&lt;T&gt;(string id).
///   - Create&lt;T&gt;(): 생성자 주입으로 순수 C# 인스턴스 생성.
///   - RefreshRegistry: seenKeys 를 HashSet&lt;RegistryKey&gt; 로 교체.
///
/// [개선 v2.1]
///   - ITickable / IFixedTickable / ILateTickable 생명주기 위임.
///     Create&lt;T&gt;() 후 해당 인터페이스 구현 여부를 확인하고 자동 등록.
///     DontDestroyOnLoad 수명 동안 Update / FixedUpdate / LateUpdate 에서 틱 제공.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MasterInstaller : MonoBehaviour, IScope
{
    private static MasterInstaller _instance;
    public static MasterInstaller Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<MasterInstaller>();
                if (_instance == null && Application.isPlaying)
                {
                    Debug.LogWarning("[MasterInstaller] Instance not found in scene. Resolve() will fail until one is created.");
                }
            }
            return _instance;
        }
    }

    // v2.0: RegistryKey = (Type, string Id) 복합 키
    private readonly Dictionary<RegistryKey, Component> _runtimeRegistry
        = new Dictionary<RegistryKey, Component>();

    [SerializeField, Tooltip("The list of Manager Layer components with [Referral] on the scene (automatically filled by Refresh Global Registry, read-only)")]
    private List<Component> _globalReferrals = new List<Component>();

    private readonly Dictionary<MonoBehaviour, List<Component>> _ownershipMap
        = new Dictionary<MonoBehaviour, List<Component>>();

    // ─── v2.1: Tickable / IScopeDestroyable 관리 ─────────────────────────────
    private readonly TickableRegistry _ticks = new TickableRegistry();

    // ─── Safety Net 상태 ───────────────────────────────────────────────────
    // true  : 다음 Resolve 미스 시 레지스트리 재구성을 1회 시도할 수 있음
    // false : 이미 시도했음. 추가 재구성을 하지 않음.
    //
    // 재무장(re-arm) 조건:
    //   명시적인 RebuildRuntimeRegistry() 호출 또는 Register() 호출
    //   → 소스 데이터가 변경되었으므로 다시 1회 시도 기회를 부여.
    private bool _safetyNetArmed = true;

    // ─── Bootstrap ───────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance == null)
        {
            _instance = FindObjectOfType<MasterInstaller>();

            if (_instance == null)
            {
                var prefab = Resources.Load<MasterInstaller>("MasterInstaller");
                if (prefab != null)
                {
                    _instance = Instantiate(prefab);
                    _instance.name = "[MasterInstaller]";
                }
            }

            if (_instance != null)
                DontDestroyOnLoad(_instance.gameObject);
        }

        if (_instance != null)
            _instance.RebuildRuntimeRegistry();
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (_runtimeRegistry.Count == 0)
                RebuildRuntimeRegistry();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        _runtimeRegistry.Clear();
        _ownershipMap.Clear();
        _ticks.ClearWithDestroy();
    }

    // ─── v2.1: Tickable 틱 ───────────────────────────────────────────────────

    private void Update()        => _ticks.Tick();
    private void FixedUpdate()   => _ticks.FixedTick();
    private void LateUpdate()    => _ticks.LateTick();

    // ─── v2.1: Tickable / IScopeDestroyable 등록·해제 ────────────────────────

    private void RegisterTickables(object obj)          => _ticks.Register(obj);
    public void UnregisterTickable(ITickable tickable)  => _ticks.Unregister(tickable);
    public void UnregisterTickable(IFixedTickable t)    => _ticks.Unregister(t);
    public void UnregisterTickable(ILateTickable t)     => _ticks.Unregister(t);

    // ─── Registry Build ───────────────────────────────────────────────────────

    /// <summary>
    /// 레지스트리를 완전히 재구성하고 Safety Net 을 다시 무장한다.
    /// 소스 데이터(_globalReferrals)가 변경되었을 때 호출됨.
    /// </summary>
    private void RebuildRuntimeRegistry()
    {
        _safetyNetArmed = true;     // 소스 변경 → 다음 미스 시 1회 재시도 허용
        RebuildRuntimeRegistryCore();
    }

    /// <summary>
    /// 실제 레지스트리 구성 로직.
    /// Safety Net 상태를 변경하지 않으므로 Safety Net 경로에서 호출해도 안전함.
    /// </summary>
    private void RebuildRuntimeRegistryCore()
    {
        _runtimeRegistry.Clear();

        foreach (var comp in _globalReferrals)
        {
            if (comp == null) continue;

            var concrete = comp.GetType();
            var referral = Attribute.GetCustomAttribute(concrete, typeof(ReferralAttribute)) as ReferralAttribute;

            InstallerRegistryHelper.RegisterTypeMappings(
                comp,
                _runtimeRegistry,
                ownerTag:         "MasterInstaller",
                bindTypeOverride: referral?.BindType,
                id:               referral?.Id ?? RegistryKey.DefaultId);
        }
    }

    // ─── Resolve API (내부 공통 경로) ─────────────────────────────────────────

    /// <summary>
    /// RegistryKey 로 컴포넌트를 조회하는 내부 메서드.
    /// Safety Net 패턴을 한 곳에서만 관리함.
    /// </summary>
    private Component ResolveByKey(RegistryKey key)
    {
        if (key.Type == null) return null;

        if (_runtimeRegistry.TryGetValue(key, out var comp) && comp != null)
            return comp;

        // Safety Net: 레지스트리 미초기화 엣지 케이스에 대한 1회 복구 시도
        if (_safetyNetArmed)
        {
            _safetyNetArmed = false;
            RebuildRuntimeRegistryCore();
            _runtimeRegistry.TryGetValue(key, out comp);
        }

        return comp;
    }

    // ─── Resolve API (IScope 구현 — 무키, v1.x 하위 호환) ────────────────────

    public Component Resolve(Type type)
        => ResolveByKey(new RegistryKey(type));

    public bool TryResolve(Type type, out Component component)
    {
        component = Resolve(type);
        return component != null;
    }

    public T Resolve<T>() where T : Component
        => Resolve(typeof(T)) as T;

    public bool TryResolve<T>(out T component) where T : Component
    {
        component = Resolve<T>();
        return component != null;
    }

    public T ResolveAs<T>() where T : class
        => Resolve(typeof(T)) as T;

    public bool TryResolveAs<T>(out T value) where T : class
    {
        value = ResolveAs<T>();
        return value != null;
    }

    public static T ResolveStatic<T>() where T : Component
        => Instance != null ? Instance.Resolve<T>() : null;

    // ─── v2.0: Named Resolve ─────────────────────────────────────────────────

    /// <summary>Named 바인딩에서 특정 Id 로 등록된 컴포넌트를 반환한다.</summary>
    public Component Resolve(Type type, string id)
        => ResolveByKey(new RegistryKey(type, id));

    public bool TryResolve(Type type, string id, out Component component)
    {
        component = Resolve(type, id);
        return component != null;
    }

    /// <summary>
    /// Named 바인딩에서 특정 Id 로 등록된 컴포넌트를 인터페이스/추상 타입으로 반환한다.
    /// 예) var enemy = MasterInstaller.Instance.ResolveAs&lt;IEnemyManager&gt;("world1");
    /// </summary>
    public T ResolveAs<T>(string id) where T : class
        => Resolve(typeof(T), id) as T;

    // ─── v1.2: IScope — 런타임 Register / Unregister ─────────────────────────

    public void Register(Component comp)
        => Register(comp, null);

    public void Register(Component comp, MonoBehaviour owner)
    {
        if (comp == null) return;

        var referral = System.Attribute.GetCustomAttribute(comp.GetType(), typeof(ReferralAttribute)) as ReferralAttribute;
        InstallerRegistryHelper.RegisterTypeMappings(
            comp,
            _runtimeRegistry,
            ownerTag:         "MasterInstaller",
            bindTypeOverride: referral?.BindType,
            id:               referral?.Id ?? RegistryKey.DefaultId);

        _safetyNetArmed = true;

        if (owner != null)
        {
            if (!_ownershipMap.TryGetValue(owner, out var list))
            {
                list = new List<Component>();
                _ownershipMap[owner] = list;
            }
            list.Add(comp);

            var tracker = owner.gameObject.GetComponent<ScopeOwnerTracker>()
                          ?? owner.gameObject.AddComponent<ScopeOwnerTracker>();
            tracker.AddDestroyCallback(() =>
                InstallerRegistryHelper.UnregisterByOwner(
                    _runtimeRegistry, _ownershipMap, owner, "MasterInstaller"));
        }

        if (!TypeDataCache.HasGeneratedGlobalPlan(comp.GetType()))
        {
            Debug.LogWarning(
                $"[MasterInstaller] Register: '{comp.GetType().Name}' has no Roslyn-generated plan. " +
                "Declare the class as 'public partial class' to enable zero-reflection injection.");
        }
    }

    /// <summary>지정 타입의 무키(기본) 등록을 전역 레지스트리에서 제거한다.</summary>
    public void Unregister(Type type)
        => Unregister(type, RegistryKey.DefaultId);

    /// <summary>v2.0: 지정 타입 + Id 의 Named 등록을 전역 레지스트리에서 제거한다.</summary>
    public void Unregister(Type type, string id)
    {
        if (type == null) return;
        var key = new RegistryKey(type, id);
        if (_runtimeRegistry.Remove(key))
            Debug.Log($"[MasterInstaller] Unregistered '{key}'.");
    }

    /// <summary>해당 컴포넌트가 등록된 모든 키(구체/인터페이스/베이스)를 한 번에 제거한다.</summary>
    public void Unregister(Component comp)
    {
        InstallerRegistryHelper.Unregister(_runtimeRegistry, comp, "MasterInstaller");
    }

    // ─── v2.0: 생성자 주입 Create<T> ─────────────────────────────────────────

    /// <summary>
    /// [InjectConstructor] 또는 단일 public 생성자를 통해 T 의 인스턴스를 생성한다.
    /// 생성자 파라미터와 [GlobalInject]/[SceneInject] 필드를 함께 주입함.
    ///
    /// 해결 우선순위: MasterInstaller(전역) → SceneInstaller(씬).
    ///
    /// 대상: ScriptableObject, 순수 C# 서비스 클래스. MonoBehaviour 에는 사용하지 않는다.
    /// </summary>
    public T Create<T>() where T : class
    {
        return InstallerRegistryHelper.CreateAndInject<T>(
            resolver: (type, id) =>
            {
                var comp = ResolveByKey(new RegistryKey(type, id));
                if (comp == null && SceneInstaller.Instance != null)
                    comp = SceneInstaller.Instance.Resolve(type, id);
                return comp;
            },
            ownerTag:  "MasterInstaller",
            onCreated: RegisterTickables);  // v2.1: ITickable 자동 등록
    }

    // ─── Editor ──────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [ContextMenu("Refresh Global Registry")]
    public void RefreshRegistry()
    {
        _globalReferrals.Clear();

        var allComponents = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
            .Where(c => c.gameObject.hideFlags == HideFlags.None);

        // v2.0: seenKeys 를 HashSet<RegistryKey> 로 교체.
        //       동일 타입이라도 Id 가 다르면 별개의 Named 바인딩으로 허용.
        var seenKeys = new HashSet<RegistryKey>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (!Attribute.IsDefined(type, typeof(ReferralAttribute))) continue;

            var referral = Attribute.GetCustomAttribute(type, typeof(ReferralAttribute)) as ReferralAttribute;
            var bindType = (referral != null && referral.BindType != null) ? referral.BindType : type;
            var key      = new RegistryKey(bindType, referral?.Id ?? RegistryKey.DefaultId);

            if (!seenKeys.Add(key))
            {
                Debug.LogWarning(
                    $"[MasterInstaller] Multiple [Referral] components mapped to '{key}' found in scene. " +
                    $"Only the first one will be used in global registry.");
                continue;
            }

            if (!_globalReferrals.Contains(comp))
                _globalReferrals.Add(comp);
        }

        RebuildRuntimeRegistry();

        EditorUtility.SetDirty(this);
        Debug.Log($"<color=yellow>[MasterInstaller]</color> Global Registry Updated. Count: {_globalReferrals.Count}");
    }

    public Component GetGlobalComponent(Type type)  => Resolve(type);
    public T         GetGlobalComponent<T>() where T : Component => Resolve<T>();
#endif
}
