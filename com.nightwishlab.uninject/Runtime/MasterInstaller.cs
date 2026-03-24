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
///    (전역 매니저 목록은 에디터에서 미리 Bake 된 결과만 사용)
///
/// [개선 v1.1]
///   - RegisterTypeMappings / IsMappableAbstraction / TryAdd →
///     InstallerRegistryHelper 로 이동. SceneInstaller 와 공유.
///   - Resolve() 의 Safety Net 이 매 호출마다 RebuildRuntimeRegistry() 를
///     무조건 실행하던 문제 수정 → armed/disarmed 패턴으로 1회만 실행.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MasterInstaller : MonoBehaviour
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

    // 런타임 전역 레지스트리 (Type -> Component)
    private readonly Dictionary<Type, Component> _runtimeRegistry = new Dictionary<Type, Component>();

    // 에디터에서 BakeUp 되는 Manager Layer 목록
    [SerializeField, Tooltip("The list of Manager Layer components with [Referral] on the scene (automatically filled by Refresh Global Registry, read-only)")]
    private List<Component> _globalReferrals = new List<Component>();

    // ─── Safety Net 상태 ───────────────────────────────────────────────────
    // true  : 다음 Resolve 미스 시 레지스트리 재구성을 1회 시도할 수 있음
    // false : 이미 시도했음. 추가 재구성을 하지 않음.
    //
    // 설계 의도:
    //   Bootstrap 타이밍 문제나 도메인 리로드 엣지 케이스에서 레지스트리가
    //   아직 채워지지 않은 상태로 Resolve 가 호출될 수 있음.
    //   이를 1회 자동 복구하되, 타입이 진짜 존재하지 않는 경우에는
    //   매 호출마다 전체 순회를 반복하지 않도록 함.
    //
    // 재무장(re-arm) 조건:
    //   명시적인 RebuildRuntimeRegistry() 호출 (Bootstrap, Awake, RefreshRegistry)
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
    }

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
                ownerTag: "MasterInstaller",
                bindTypeOverride: referral?.BindType);
        }
    }

    // ─── Resolve API ─────────────────────────────────────────────────────────

    /// <summary>
    /// 런타임 전역 레지스트리에서 타입에 해당하는 컴포넌트를 반환한다.
    ///
    /// Safety Net:
    ///   조회 미스 발생 시, 레지스트리가 미초기화 상태일 가능성에 대비해
    ///   단 1회 재구성을 시도한다.
    ///   이미 시도했거나 재구성 후에도 없으면 null 을 반환한다.
    ///   → 타입이 진짜 없는 경우 매 호출마다 전체 순회하는 문제를 방지.
    /// </summary>
    public Component Resolve(Type type)
    {
        if (type == null) return null;

        if (_runtimeRegistry.TryGetValue(type, out var comp) && comp != null)
            return comp;

        // Safety Net: 레지스트리 미초기화 엣지 케이스에 대한 1회 복구 시도
        if (_safetyNetArmed)
        {
            _safetyNetArmed = false;            // 무장 해제 (이후 미스에서는 재구성 없음)
            RebuildRuntimeRegistryCore();       // 상태 변경 없는 코어 재구성
            _runtimeRegistry.TryGetValue(type, out comp);
        }

        return comp;
    }

    public bool TryResolve(Type type, out Component component)
    {
        component = Resolve(type);
        return component != null;
    }

    public T Resolve<T>() where T : Component
    {
        return Resolve(typeof(T)) as T;
    }

    public bool TryResolve<T>(out T component) where T : Component
    {
        component = Resolve<T>();
        return component != null;
    }

    /// <summary>
    /// 인터페이스/추상 타입을 포함해 조회 가능한 헬퍼.
    /// 예) var input = MasterInstaller.Instance.ResolveAs&lt;IInputManager&gt;();
    /// </summary>
    public T ResolveAs<T>() where T : class
    {
        return Resolve(typeof(T)) as T;
    }

    public bool TryResolveAs<T>(out T value) where T : class
    {
        value = ResolveAs<T>();
        return value != null;
    }

    public static T ResolveStatic<T>() where T : Component
    {
        return Instance != null ? Instance.Resolve<T>() : null;
    }

    // ─── Editor ──────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>
    /// 에디터(PlayMode 아님)에서 현재 씬의 [Referral] 컴포넌트들을 스캔하여
    /// _globalReferrals 리스트를 갱신하고, 레지스트리를 재구성한다.
    /// ObjectInstaller.BakeDependencies 에서도 호출될 수 있음.
    /// </summary>
    [ContextMenu("Refresh Global Registry")]
    public void RefreshRegistry()
    {
        _globalReferrals.Clear();

        var allComponents = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
            .Where(c => c.gameObject.hideFlags == HideFlags.None);

        var seenKeys = new HashSet<Type>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (!Attribute.IsDefined(type, typeof(ReferralAttribute))) continue;

            var referral = Attribute.GetCustomAttribute(type, typeof(ReferralAttribute)) as ReferralAttribute;
            var key = (referral != null && referral.BindType != null) ? referral.BindType : type;

            if (!seenKeys.Add(key))
            {
                Debug.LogWarning(
                    $"[MasterInstaller] Multiple [Referral] components mapped to '{key.Name}' found in scene. " +
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

    /// <summary>
    /// 에디터 Bake 용 헬퍼: 타입에 맞는 Manager 컴포넌트를 반환한다.
    /// </summary>
    public Component GetGlobalComponent(Type type) => Resolve(type);

    public T GetGlobalComponent<T>() where T : Component => Resolve<T>();
#endif
}