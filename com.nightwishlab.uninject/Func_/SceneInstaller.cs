using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 현재 씬 범위에서만 유효한 Manager Layer([SceneReferral])를
/// 전역처럼 제공하는 씬 전용 레지스트리입니다.
/// 
/// - MasterInstaller 가 게임 전체 전역 매니저를 담당한다면,
///   SceneInstaller 는 해당 씬 한정 매니저를 담당합니다.
/// - DontDestroyOnLoad 를 사용하지 않으므로, 씬 전환 시 함께 교체됩니다.
/// </summary>
[DefaultExecutionOrder(-900)]
public class SceneInstaller : MonoBehaviour
{
    private static SceneInstaller _instance;
    public static SceneInstaller Instance => _instance;

    // 런타임 씬 전역 레지스트리 (Type -> Component)
    private readonly Dictionary<Type, Component> _sceneRegistry = new Dictionary<Type, Component>();

    // 에디터에서 BakeUp 되는 씬 전용 Manager Layer 목록 (디버그용)
    [SerializeField, Tooltip("이 씬에서 [SceneReferral] 이 붙은 Manager Layer 컴포넌트들 (Refresh Scene Registry로 자동 채움, 읽기 전용)")]
    private List<Component> _sceneReferrals = new List<Component>();

    private void Awake()
    {
        _instance = this;

        if (_sceneRegistry.Count == 0 && _sceneReferrals.Count > 0)
        {
            RebuildSceneRegistry();
        }
    }

    private void OnDestroy()
    {
        // 현재 인스턴스가 전역 Instance 로 등록되어 있다면 정리
        if (_instance == this)
        {
            _instance = null;
        }

        // 레지스트리와 참조 리스트 비워서 GC 가 수거할 수 있도록 함
        _sceneRegistry.Clear();
        _sceneReferrals.Clear();
    }

    /// <summary>
    /// 에디터에서 BakeUp 된 _sceneReferrals 리스트를 기반으로
    /// 런타임 씬 전역 레지스트리(Type -> Component)를 재구성합니다.
    /// </summary>
    private void RebuildSceneRegistry()
    {
        _sceneRegistry.Clear();

        foreach (var comp in _sceneReferrals)
        {
            if (comp == null) continue;
            RegisterTypeMappings(comp, _sceneRegistry, ownerTag: "SceneInstaller");
        }
    }

    /// <summary>
    /// 하나의 컴포넌트를 다음 타입 키들로 레지스트리에 등록합니다.
    /// - 자기 자신(구체 타입)
    /// - 구현한 인터페이스들
    /// - 상속 체인의 베이스 타입들(너무 광범위한 Unity 공통 베이스는 제외)
    ///
    /// 목적: [SceneInject]가 인터페이스/추상 타입 필드를 대상으로 하더라도 Resolve 가능하게 함.
    /// </summary>
    private static void RegisterTypeMappings(
        Component comp,
        Dictionary<Type, Component> registry,
        string ownerTag)
    {
        if (comp == null || registry == null)
            return;

        var concrete = comp.GetType();

        // [SceneReferral(BindType)] 지정 시: 지정된 타입만 Key 로 등록 (구체 타입/전체 인터페이스 매핑은 생략)
        var referral = Attribute.GetCustomAttribute(concrete, typeof(SceneReferralAttribute)) as SceneReferralAttribute;
        if (referral != null && referral.BindType != null)
        {
            var bindType = referral.BindType;
            if (!bindType.IsAssignableFrom(concrete))
            {
                Debug.LogWarning(
                    $"[{ownerTag}] SceneReferral BindType '{bindType.FullName}' is not assignable from '{concrete.FullName}'. " +
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

        // 너무 광범위한 타입은 매핑 금지
        if (type == typeof(object))
            return false;

        // 흔한 System/Unity 인터페이스는 매핑하지 않음 (충돌/오염 방지)
        // - 예: IDisposable, IEnumerable, UI 이벤트 인터페이스(IPointerDownHandler 등)
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
    /// 씬 전역 레지스트리에서 타입에 해당하는 컴포넌트를 반환합니다.
    /// </summary>
    public Component Resolve(Type type)
    {
        if (type == null) return null;

        if (_sceneRegistry.TryGetValue(type, out var comp) && comp != null)
        {
            return comp;
        }

        return null;
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
    /// 예) var input = SceneInstaller.Instance.ResolveAs&lt;IPlayerInput&gt;();
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

#if UNITY_EDITOR
    /// <summary>
    /// 에디터(PlayMode 아님)에서 현재 씬의 [SceneReferral] 컴포넌트들을 스캔하여
    /// _sceneReferrals 리스트를 갱신하고, 그 결과로 씬 레지스트리를 재구성합니다.
    /// </summary>
    [ContextMenu("Refresh Scene Registry")]
    public void RefreshSceneRegistry()
    {
        _sceneReferrals.Clear();

        var currentScene = gameObject.scene;

        // 씬 내 모든 컴포넌트 스캔 (비활성 포함)
        var allComponents = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c.gameObject.scene == currentScene)
            .Where(c => c.gameObject.hideFlags == HideFlags.None);

        var seenKeys = new HashSet<Type>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (Attribute.IsDefined(type, typeof(SceneReferralAttribute)))
            {
                var referral = Attribute.GetCustomAttribute(type, typeof(SceneReferralAttribute)) as SceneReferralAttribute;
                var key = (referral != null && referral.BindType != null) ? referral.BindType : type;

                if (!seenKeys.Add(key))
                {
                    Debug.LogWarning(
                        $"[SceneInstaller] Multiple [SceneReferral] components mapped to '{key.Name}' found in scene. " +
                        $"Only the first one will be used in scene registry.");
                    continue;
                }

                if (!_sceneReferrals.Contains(comp))
                {
                    _sceneReferrals.Add(comp);
                }
            }
        }

        RebuildSceneRegistry();

        EditorUtility.SetDirty(this);
        Debug.Log($"<color=#a6e3ff>[SceneInstaller]</color> Scene Registry Updated. Count: {_sceneReferrals.Count}");
    }
#endif
}

