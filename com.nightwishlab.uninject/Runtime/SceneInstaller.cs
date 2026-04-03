using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 현재 씬 범위에서만 유효한 Manager Layer([SceneReferral])를
/// 전역처럼 제공하는 씬 전용 레지스트리.
///
/// - MasterInstaller 가 게임 전체 전역 매니저를 담당한다면,
///   SceneInstaller 는 해당 씬 한정 매니저를 담당함.
/// - DontDestroyOnLoad 를 사용하지 않으므로, 씬 전환 시 함께 교체됨.
///
/// [개선 v1.1]
///   - 공통 로직 → InstallerRegistryHelper.
///
/// [개선 v2.0]
///   - 레지스트리: Dictionary&lt;Type, Component&gt; → Dictionary&lt;RegistryKey, Component&gt;.
///   - Named/Keyed Resolve: Resolve(Type, string id), ResolveAs&lt;T&gt;(string id).
///   - Create&lt;T&gt;(): 생성자 주입으로 순수 C# 인스턴스 생성.
///   - Safety Net: Resolve 미스 시 1회 복구 시도 (MasterInstaller 와 동일 패턴).
///   - Awake 에서 중복 인스턴스 경고 추가.
///   - SceneExitPolicy.Preserve 시 _ownershipMap 을 의도적으로 초기화.
///
/// [개선 v2.1]
///   - ITickable / IFixedTickable / ILateTickable 생명주기 위임.
///   - _tickables 는 SceneExitPolicy 와 무관하게 OnDestroy 에서 항상 초기화.
///     씬 오브젝트가 파괴된 뒤에도 틱을 받는 것은 대부분 버그이기 때문.
/// </summary>
[DefaultExecutionOrder(-900)]
public class SceneInstaller : MonoBehaviour, IScope
{
    private static SceneInstaller _instance;
    public static SceneInstaller Instance => _instance;

    // v2.0: RegistryKey = (Type, string Id) 복합 키
    private readonly Dictionary<RegistryKey, Component> _sceneRegistry
        = new Dictionary<RegistryKey, Component>();

    [SerializeField, Tooltip("The list of Manager Layer components with [SceneReferral] on the scene (automatically filled by Refresh Scene Registry, read-only)")]
    private List<Component> _sceneReferrals = new List<Component>();

    [SerializeField, Tooltip(
        "Clear: OnDestroy 시 씬 레지스트리를 초기화 (기본값).\n" +
        "Preserve: 씬 언로드 후에도 레지스트리를 메모리에 유지 (Additive 씬 로딩 등에 활용).")]
    private SceneExitPolicy _exitPolicy = SceneExitPolicy.Clear;

    private readonly Dictionary<MonoBehaviour, List<Component>> _ownershipMap
        = new Dictionary<MonoBehaviour, List<Component>>();

    // ─── v2.1: Tickable / IScopeDestroyable 관리 ─────────────────────────────
    private readonly TickableRegistry _ticks = new TickableRegistry();

    // ─── Safety Net 상태 ────────────────────────────────────────────────────
    // true  : 다음 Resolve 미스 시 레지스트리 재구성을 1회 시도할 수 있음
    // false : 이미 시도했음.
    //
    // 재무장(re-arm) 조건: RebuildSceneRegistry() 또는 Register() 호출.
    private bool _safetyNetArmed = true;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning(
                $"[SceneInstaller] Multiple SceneInstaller instances detected. " +
                $"'{_instance.gameObject.name}' (scene: '{_instance.gameObject.scene.name}') " +
                $"will be replaced by '{gameObject.name}' (scene: '{gameObject.scene.name}'). " +
                "In Additive loading, ensure only one SceneInstaller is active at a time, " +
                "or use SceneExitPolicy.Preserve on the outgoing scene.");
        }

        _instance = this;

        if (_sceneRegistry.Count == 0 && _sceneReferrals.Count > 0)
            RebuildSceneRegistry();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        if (_exitPolicy == SceneExitPolicy.Clear)
        {
            _sceneRegistry.Clear();
            _sceneReferrals.Clear();
            _ownershipMap.Clear();
        }
        else // SceneExitPolicy.Preserve
        {
            // _sceneRegistry 는 씬 전환 과도기를 위해 의도적으로 보존.
            //
            // _ownershipMap 은 씬 오브젝트 수명에 묶여 있으므로 여기서 초기화한다.
            // ScopeOwnerTracker.OnDestroy 콜백이 씬 언로드 시 발화할 때,
            // ownershipMap 이 비워진 상태면 UnregisterByOwner 가 조기 반환하여
            // 보존된 registry 항목을 제거하지 않는다. (의도된 동작)
            _ownershipMap.Clear();
            _sceneReferrals.Clear();
        }

        // v2.1: Tickable / IScopeDestroyable 은 씬 오브젝트 수명에 종속됨.
        //       SceneExitPolicy 와 무관하게 항상 정리 — 씬 파괴 후 틱·콜백은 버그.
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

    private void RebuildSceneRegistry()
    {
        _safetyNetArmed = true;
        RebuildSceneRegistryCore();
    }

    private void RebuildSceneRegistryCore()
    {
        _sceneRegistry.Clear();

        foreach (var comp in _sceneReferrals)
        {
            if (comp == null) continue;

            var concrete = comp.GetType();
            var referral = Attribute.GetCustomAttribute(concrete, typeof(SceneReferralAttribute)) as SceneReferralAttribute;

            InstallerRegistryHelper.RegisterTypeMappings(
                comp,
                _sceneRegistry,
                ownerTag:         "SceneInstaller",
                bindTypeOverride: referral?.BindType,
                id:               referral?.Id ?? RegistryKey.DefaultId);
        }
    }

    // ─── Resolve API (내부 공통 경로) ─────────────────────────────────────────

    private Component ResolveByKey(RegistryKey key)
    {
        if (key.Type == null) return null;

        if (_sceneRegistry.TryGetValue(key, out var comp) && comp != null)
            return comp;

        if (_safetyNetArmed)
        {
            _safetyNetArmed = false;
            RebuildSceneRegistryCore();
            _sceneRegistry.TryGetValue(key, out comp);
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

    // ─── v2.0: Named Resolve ─────────────────────────────────────────────────

    public Component Resolve(Type type, string id)
        => ResolveByKey(new RegistryKey(type, id));

    public bool TryResolve(Type type, string id, out Component component)
    {
        component = Resolve(type, id);
        return component != null;
    }

    public T ResolveAs<T>(string id) where T : class
        => Resolve(typeof(T), id) as T;

    // ─── v1.2: IScope — 런타임 Register / Unregister ─────────────────────────

    public void Register(Component comp)
        => Register(comp, null);

    public void Register(Component comp, MonoBehaviour owner)
    {
        if (comp == null) return;

        var referral = System.Attribute.GetCustomAttribute(comp.GetType(), typeof(SceneReferralAttribute)) as SceneReferralAttribute;
        InstallerRegistryHelper.RegisterTypeMappings(
            comp,
            _sceneRegistry,
            ownerTag:         "SceneInstaller",
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
                    _sceneRegistry, _ownershipMap, owner, "SceneInstaller"));
        }

        if (!TypeDataCache.HasGeneratedScenePlan(comp.GetType()))
        {
            Debug.LogWarning(
                $"[SceneInstaller] Register: '{comp.GetType().Name}' has no Roslyn-generated plan. " +
                "Declare the class as 'public partial class' to enable zero-reflection injection.");
        }
    }

    public void Unregister(Type type)
        => Unregister(type, RegistryKey.DefaultId);

    public void Unregister(Type type, string id)
    {
        if (type == null) return;
        var key = new RegistryKey(type, id);
        if (_sceneRegistry.Remove(key))
            Debug.Log($"[SceneInstaller] Unregistered '{key}'.");
    }

    public void Unregister(Component comp)
    {
        InstallerRegistryHelper.Unregister(_sceneRegistry, comp, "SceneInstaller");
    }

    // ─── v2.0: 생성자 주입 Create<T> ─────────────────────────────────────────

    /// <summary>
    /// [InjectConstructor] 또는 단일 public 생성자를 통해 T 의 인스턴스를 생성한다.
    /// 생성자 파라미터와 [GlobalInject]/[SceneInject] 필드를 함께 주입함.
    ///
    /// 해결 우선순위: SceneInstaller(씬) → MasterInstaller(전역).
    /// </summary>
    public T Create<T>() where T : class
    {
        return InstallerRegistryHelper.CreateAndInject<T>(
            resolver: (type, id) =>
            {
                var comp = ResolveByKey(new RegistryKey(type, id));
                if (comp == null && MasterInstaller.Instance != null)
                    comp = MasterInstaller.Instance.Resolve(type, id);
                return comp;
            },
            ownerTag:  "SceneInstaller",
            onCreated: RegisterTickables);  // v2.1: ITickable 자동 등록
    }

    // ─── Editor ──────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [ContextMenu("Refresh Scene Registry")]
    public void RefreshSceneRegistry()
    {
        _sceneReferrals.Clear();

        var currentScene = gameObject.scene;

        var allComponents = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c.gameObject.scene == currentScene)
            .Where(c => c.gameObject.hideFlags == HideFlags.None);

        // v2.0: seenKeys 를 HashSet<RegistryKey> 로 교체.
        var seenKeys = new HashSet<RegistryKey>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (!Attribute.IsDefined(type, typeof(SceneReferralAttribute))) continue;

            var referral = Attribute.GetCustomAttribute(type, typeof(SceneReferralAttribute)) as SceneReferralAttribute;
            var bindType = (referral != null && referral.BindType != null) ? referral.BindType : type;
            var key      = new RegistryKey(bindType, referral?.Id ?? RegistryKey.DefaultId);

            if (!seenKeys.Add(key))
            {
                Debug.LogWarning(
                    $"[SceneInstaller] Multiple [SceneReferral] components mapped to '{key}' found in scene. " +
                    $"Only the first one will be used in scene registry.");
                continue;
            }

            if (!_sceneReferrals.Contains(comp))
                _sceneReferrals.Add(comp);
        }

        RebuildSceneRegistry();     // 내부에서 _safetyNetArmed = true 처리됨

        EditorUtility.SetDirty(this);
        Debug.Log($"<color=#a6e3ff>[SceneInstaller]</color> Scene Registry Updated. Count: {_sceneReferrals.Count}");
    }
#endif
}
