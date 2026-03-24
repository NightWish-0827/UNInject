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
///   - RegisterTypeMappings / IsMappableAbstraction / TryAdd →
///     InstallerRegistryHelper 로 이동. MasterInstaller 와 공유.
/// </summary>
[DefaultExecutionOrder(-900)]
public class SceneInstaller : MonoBehaviour
{
    private static SceneInstaller _instance;
    public static SceneInstaller Instance => _instance;

    // 런타임 씬 전역 레지스트리 (Type -> Component)
    private readonly Dictionary<Type, Component> _sceneRegistry = new Dictionary<Type, Component>();

    // 에디터에서 BakeUp 되는 씬 전용 Manager Layer 목록
    [SerializeField, Tooltip("The list of Manager Layer components with [SceneReferral] on the scene (automatically filled by Refresh Scene Registry, read-only)")]
    private List<Component> _sceneReferrals = new List<Component>();

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;

        if (_sceneRegistry.Count == 0 && _sceneReferrals.Count > 0)
            RebuildSceneRegistry();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        _sceneRegistry.Clear();
        _sceneReferrals.Clear();
    }

    // ─── Registry Build ───────────────────────────────────────────────────────

    /// <summary>
    /// 에디터에서 BakeUp 된 _sceneReferrals 리스트를 기반으로
    /// 런타임 씬 전역 레지스트리(Type -> Component)를 재구성한다.
    /// </summary>
    private void RebuildSceneRegistry()
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
                ownerTag: "SceneInstaller",
                bindTypeOverride: referral?.BindType);
        }
    }

    // ─── Resolve API ─────────────────────────────────────────────────────────

    public Component Resolve(Type type)
    {
        if (type == null) return null;

        _sceneRegistry.TryGetValue(type, out var comp);
        return (comp != null) ? comp : null;
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

    public T ResolveAs<T>() where T : class
    {
        return Resolve(typeof(T)) as T;
    }

    public bool TryResolveAs<T>(out T value) where T : class
    {
        value = ResolveAs<T>();
        return value != null;
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

        var seenKeys = new HashSet<Type>();

        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            var type = comp.GetType();
            if (!Attribute.IsDefined(type, typeof(SceneReferralAttribute))) continue;

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
                _sceneReferrals.Add(comp);
        }

        RebuildSceneRegistry();

        EditorUtility.SetDirty(this);
        Debug.Log($"<color=#a6e3ff>[SceneInstaller]</color> Scene Registry Updated. Count: {_sceneReferrals.Count}");
    }
#endif
}