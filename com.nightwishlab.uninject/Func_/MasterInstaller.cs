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
                // 런타임에서만 경고를 출력하고, 에디터에서는 조용히 null 반환
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

    // 에디터에서 BakeUp 되는 Manager Layer 목록 (읽기 전용 직렬화 필드)
    [SerializeField, Tooltip("The list of Manager Layer components with [Referral] on the scene (automatically filled by Refresh Global Registry, read-only)")]
    private List<Component> _globalReferrals = new List<Component>();

    // 최초 씬이 로드되기 직전에 레지스트리 구성
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance == null)
        {
            _instance = FindObjectOfType<MasterInstaller>();

            if (_instance == null)
            {
                // 선택 사항: Resources/MasterInstaller 프리팹에서 자동 생성
                var prefab = Resources.Load<MasterInstaller>("MasterInstaller");
                if (prefab != null)
                {
                    _instance = Instantiate(prefab);
                    _instance.name = "[MasterInstaller]";
                }
            }

            if (_instance != null)
            {
                DontDestroyOnLoad(_instance.gameObject);
            }
        }

        if (_instance != null)
        {
            _instance.RebuildRuntimeRegistry();
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (_runtimeRegistry.Count == 0)
            {
                RebuildRuntimeRegistry();
            }
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // 도메인 리로드 비활성 + PlayMode 전환 등에서 정적 Instance 오염 방지
        if (_instance == this)
        {
            _instance = null;
        }

        _runtimeRegistry.Clear();
    }

    /// <summary>
    /// 에디터에서 BakeUp 된 _globalReferrals 리스트를 기반으로
    /// 런타임 전역 레지스트리(Type -> Component)를 재구성함.
    /// </summary>
    private void RebuildRuntimeRegistry()
    {
        _runtimeRegistry.Clear();

        foreach (var comp in _globalReferrals)
        {
            if (comp == null) continue;
            RegisterTypeMappings(comp, _runtimeRegistry, ownerTag: "MasterInstaller");
        }
    }

    /// <summary>
    /// 하나의 컴포넌트를 다음 타입 키들로 레지스트리에 등록함.
    /// - 자기 자신(구체 타입)
    /// - 구현한 인터페이스들
    /// - 상속 체인의 베이스 타입들(너무 광범위한 Unity 공통 베이스는 제외)
    ///
    /// 목적: [GlobalInject]가 인터페이스/추상 타입 필드를 대상으로 하더라도 Resolve 가능하게 함.
    /// </summary>
    private static void RegisterTypeMappings(
        Component comp,
        Dictionary<Type, Component> registry,
        string ownerTag)
    {
        if (comp == null || registry == null)
            return;

        var concrete = comp.GetType();

        // [Referral(BindType)] 지정 시: 지정된 타입만 Key 로 등록 (구체 타입/전체 인터페이스 매핑은 생략)
        var referral = Attribute.GetCustomAttribute(concrete, typeof(ReferralAttribute)) as ReferralAttribute;
        if (referral != null && referral.BindType != null)
        {
            var bindType = referral.BindType;
            if (!bindType.IsAssignableFrom(concrete))
            {
                Debug.LogWarning(
                    $"[{ownerTag}] Referral BindType '{bindType.FullName}' is not assignable from '{concrete.FullName}'. " +
                    $"Falling back to concrete type mapping for '{concrete.Name}'.");
            }
            else
            {
                TryAdd(registry, bindType, comp, ownerTag);
                return;
            }
        }

        // 1) 구체 타입
        TryAdd(registry, concrete, comp, ownerTag);

        // 2) 인터페이스
        foreach (var itf in concrete.GetInterfaces())
        {
            if (IsMappableAbstraction(itf))
            {
                TryAdd(registry, itf, comp, ownerTag);
            }
        }

        // 3) 베이스 타입 (Component/Behaviour/MonoBehaviour/object 등 너무 넓은 타입은 제외)
        var baseType = concrete.BaseType;
        while (baseType != null &&
               baseType != typeof(object) &&
               baseType != typeof(Component) &&
               baseType != typeof(Behaviour) &&
               baseType != typeof(MonoBehaviour))
        {
            TryAdd(registry, baseType, comp, ownerTag);
            baseType = baseType.BaseType;
        }
    }

    private static bool IsMappableAbstraction(Type type)
    {
        if (type == null)
            return false;

        if (type == typeof(object))
            return false;

        var ns = type.Namespace ?? string.Empty;
        if (ns == "System" || ns.StartsWith("System."))
            return false;
        if (ns == "UnityEngine" || ns.StartsWith("UnityEngine."))
            return false;
        if (ns == "UnityEditor" || ns.StartsWith("UnityEditor."))
            return false;
        if (ns == "Unity" || ns.StartsWith("Unity."))
            return false;

        return true;
    }

    private static void TryAdd(
        Dictionary<Type, Component> registry,
        Type key,
        Component value,
        string ownerTag)
    {
        if (key == null || value == null)
            return;

        if (registry.TryGetValue(key, out var existing) && existing != null && existing != value)
        {
            Debug.LogWarning(
                $"[{ownerTag}] Duplicate mapping for type '{key.FullName}'. " +
                $"Keeping '{existing.name}' ({existing.GetType().Name}), ignoring '{value.name}' ({value.GetType().Name}).");
            return;
        }

        registry[key] = value;
    }

    /// <summary>
    /// 런타임 전역 레지스트리에서 타입에 해당하는 컴포넌트를 반환함.
    /// 필요 시 한 번 더 레지스트리를 재구성하여 안전망을 제공함.
    /// </summary>
    public Component Resolve(Type type)
    {
        if (type == null) return null;

        if (!_runtimeRegistry.TryGetValue(type, out var comp) || comp == null)
        {
            // 안전망: 레지스트리를 다시 구성해 본다.
            RebuildRuntimeRegistry();
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

#if UNITY_EDITOR
    /// <summary>
    /// 에디터(PlayMode 아님)에서 현재 씬의 [Referral] 컴포넌트들을 스캔하여
    /// _globalReferrals 리스트를 갱신하고, 그 결과로 런타임 레지스트리를 재구성.
    /// ObjectInstaller.BakeDependencies 에서도 호출될 수 있음.
    /// </summary>
    [ContextMenu("Refresh Global Registry")]
    public void RefreshRegistry()
    {
        _globalReferrals.Clear();

        // 씬 내 모든 컴포넌트 스캔 (비활성 포함, 에셋/프리팹 제외)
        var allComponents = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
            .Where(c => c.gameObject.hideFlags == HideFlags.None);

        var seenKeys = new HashSet<Type>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (Attribute.IsDefined(type, typeof(ReferralAttribute)))
            {
                // BindType 이 지정된 경우, Concrete 가 아닌 BindType 을 중복 키 기준으로 사용
                var referral = Attribute.GetCustomAttribute(type, typeof(ReferralAttribute)) as ReferralAttribute;
                var key = (referral != null && referral.BindType != null) ? referral.BindType : type;

                // 같은 키의 [Referral] 이 여러 개 있으면 경고 (첫 번째만 사용)
                if (!seenKeys.Add(key))
                {
                    Debug.LogWarning(
                        $"[MasterInstaller] Multiple [Referral] components mapped to '{key.Name}' found in scene. " +
                        $"Only the first one will be used in global registry.");
                    continue;
                }

                if (!_globalReferrals.Contains(comp))
                {
                    _globalReferrals.Add(comp);
                }
            }
        }

        // Bake된 리스트를 기반으로 런타임 레지스트리 재구성
        RebuildRuntimeRegistry();

        EditorUtility.SetDirty(this);
        Debug.Log($"<color=yellow>[MasterInstaller]</color> Global Registry Updated. Count: {_globalReferrals.Count}");
    }

    /// <summary>
    /// 에디터 Bake 용 헬퍼: 타입에 맞는 Manager 컴포넌트를 반환함.
    /// (내부적으로 Resolve 를 사용)
    /// </summary>
    public Component GetGlobalComponent(Type type)
    {
        return Resolve(type);
    }

    public T GetGlobalComponent<T>() where T : Component
        => Resolve<T>();
#endif
}