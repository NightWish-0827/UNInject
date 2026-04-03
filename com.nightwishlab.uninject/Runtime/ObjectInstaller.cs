using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 특정 루트 GameObject 에 붙어서,
/// 그 자식 계층 내부의 의존성을 관리하는 컴포넌트.
///
/// - [Inject]       : 같은 루트 계층 내의 컴포넌트를 찾아 에디터에서 베이크
/// - [GlobalInject] : MasterInstaller 가 관리하는 전역 Manager 를 런타임에 주입
/// - [SceneInject]  : SceneInstaller 가 관리하는 씬 전용 Manager 를 런타임에 주입
///
/// [v1.2] IScope 구현:
///   - 로컬 레지스트리(_localRegistry) 를 통해 서브트리 전용 컴포넌트를 등록/조회할 수 있음.
///   - Resolve 체인 : 로컬 → _parentScope (또는 SceneInstaller) → MasterInstaller
///   - SpawnInjected : Instantiate + InjectGameObject 를 하나의 API 로 제공.
///   - InjectTargetFromPool / ReleaseTargetToPool : 풀 사이클과 주입을 연동.
///
/// [v2.0]
///   - 로컬 레지스트리: Dictionary&lt;Type, Component&gt; → Dictionary&lt;RegistryKey, Component&gt;.
///   - TryInjectTarget 이 field.Id 로 Named 바인딩을 해결함.
///   - Named Resolve: Resolve(Type, string id), ResolveAs&lt;T&gt;(string id).
///   - Create&lt;T&gt;(): 생성자 주입 + 필드 주입.
///   - _parentScope: 명시적 상위 스코프 (null 이면 기본 체인 사용).
///   - Register&lt;TBind&gt;: 코드 기반 명시적 BindType 오버로드.
///
/// [v2.1]
///   - ITickable / IFixedTickable / ILateTickable 생명주기 위임.
///   - 틱은 등록된 스코프에서만 발생; 부모 스코프로 전파되지 않음.
/// </summary>
[DefaultExecutionOrder(-500)]
public class ObjectInstaller : MonoBehaviour, IScope
{
    // ─── v1.2: 로컬 스코프 레지스트리 ───────────────────────────────────────
    // v2.0: RegistryKey 복합 키 사용
    private readonly Dictionary<RegistryKey, Component> _localRegistry
        = new Dictionary<RegistryKey, Component>();

    private readonly Dictionary<MonoBehaviour, List<Component>> _ownershipMap
        = new Dictionary<MonoBehaviour, List<Component>>();

    // ─── v2.1: Tickable 관리 ─────────────────────────────────────────────────
    // 틱은 등록된 ObjectInstaller 스코프에서만 발생.
    // 부모 스코프로의 전파 없음 — 예측 가능성과 단순성을 위한 정책.
    // ─── v2.1: Tickable / IScopeDestroyable 관리 ─────────────────────────────
    private readonly TickableRegistry _ticks = new TickableRegistry();

    // ─── v2.0: 명시적 부모 스코프 ────────────────────────────────────────────
    // null 이면 기본 체인(SceneInstaller 싱글톤 → MasterInstaller 싱글톤)을 사용.
    // 설정 시, 싱글톤 대신 지정된 ObjectInstaller 를 상위로 사용하며
    // 해당 ObjectInstaller 의 Resolve 체인을 그대로 따름.
    // 용도: 풀 관리자, 서브시스템별 격리 스코프, 중첩 ObjectInstaller 계층 구성.
    [SerializeField, Tooltip(
        "상위 스코프를 명시적으로 지정합니다. null 이면 SceneInstaller → MasterInstaller 기본 체인을 사용합니다.\n" +
        "중첩 ObjectInstaller 구조에서 스코프 계층을 직접 구성할 때 사용하세요.")]
    private ObjectInstaller _parentScope;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        InjectGlobalDependencies();
    }

    private void OnDestroy()
    {
        _localRegistry.Clear();
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

    private void InjectGlobalDependencies()
    {
        var targets = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var target in targets)
        {
            if (target == null) continue;
            // TypeDataCache 에서 주입 필드가 없다고 확인된 타입은 건너뜀.
            // GetGlobalInjectFields / GetSceneInjectFields 가 이미 캐싱하므로
            // 첫 호출 이후 이 분기는 O(1) 딕셔너리 조회만 수행함.
            if (!TypeDataCache.HasAnyInjectField(target.GetType())) continue;
            InjectTarget(target);
        }
    }

    // ─── 주입 API ────────────────────────────────────────────────────────────

    public void InjectTarget(MonoBehaviour target)
    {
        TryInjectTarget(target, logWarnings: true);
    }

    /// <summary>
    /// 주입 성공/실패를 반환하는 수동 주입 API.
    ///
    /// v2.0: field.Id 를 Resolve 에 전달하여 Named 바인딩을 정확히 해결함.
    /// </summary>
    public bool TryInjectTarget(MonoBehaviour target, bool logWarnings = true, bool isReinjection = false)
    {
        if (target == null) return false;

        var type    = target.GetType();
        bool success = true;

#if UNINJECT_PROFILING
        var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

        // 1) 씬 전용 매니저 주입 ([SceneInject])
        var sceneFields = TypeDataCache.GetSceneInjectFields(type);
        foreach (var field in sceneFields)
        {
            // v2.0: field.Id 를 전달하여 Named 바인딩 해결
            var resolvedScene = Resolve(field.FieldType, field.Id);
            if (resolvedScene != null)
            {
                field.Setter(target, resolvedScene);
            }
            else
            {
                if (!field.Optional)
                {
                    success = false;
                    if (logWarnings)
                        Debug.LogWarning(
                            $"[ObjectInstaller] Failed to resolve SceneInject for field " +
                            $"'{field.Name}' ({field.FieldType.Name}" +
                            $"{(string.IsNullOrEmpty(field.Id) ? "" : $", id: '{field.Id}'")})" +
                            $" on {target.name}");
                }
            }
        }

        // 2) 전역 매니저 주입 ([GlobalInject])
        var globalFields = TypeDataCache.GetGlobalInjectFields(type);
        foreach (var field in globalFields)
        {
            var resolved = Resolve(field.FieldType, field.Id);
            if (resolved != null)
            {
                field.Setter(target, resolved);
            }
            else
            {
                if (!field.Optional)
                {
                    success = false;
                    if (logWarnings)
                        Debug.LogWarning(
                            $"[ObjectInstaller] Failed to resolve GlobalInject for field " +
                            $"'{field.Name}' ({field.FieldType.Name}" +
                            $"{(string.IsNullOrEmpty(field.Id) ? "" : $", id: '{field.Id}'")})" +
                            $" on {target.name}");
                }
            }
        }

#if UNINJECT_PROFILING
        sw.Stop();
        UNInjectProfiler.RecordInjection(type, sw.Elapsed.TotalMilliseconds);
#endif

        if (success)
        {
            if (!isReinjection && target is IInjected injected)
                injected.OnInjected();
            else if (isReinjection && target is IPoolInjectionTarget poolTarget)
                poolTarget.OnPoolGet();
        }

        return success;
    }

    public void InjectGameObject(GameObject root, bool includeInactive = true)
    {
        if (root == null) return;
        var targets = root.GetComponentsInChildren<MonoBehaviour>(includeInactive);
        foreach (var target in targets)
            InjectTarget(target);
    }

    // ─── v1.2: IScope 구현 ───────────────────────────────────────────────────

    public void Register(Component comp)
        => RegisterInternal(comp, null, null);

    public void Register(Component comp, MonoBehaviour owner)
        => RegisterInternal(comp, owner, null);

    /// <summary>
    /// v2.0: 명시적 BindType 오버로드.
    /// 어트리뷰트 없이 코드로 인터페이스 바인딩을 지정할 때 사용한다.
    /// [Referral(typeof(IFoo))] 없이도 localScope.Register&lt;IFoo&gt;(fooImpl) 가능.
    /// </summary>
    public void Register<TBind>(Component comp, MonoBehaviour owner = null) where TBind : class
        => RegisterInternal(comp, owner, typeof(TBind));

    private void RegisterInternal(Component comp, MonoBehaviour owner, Type bindTypeOverride)
    {
        if (comp == null) return;

        // v2.0: MasterInstaller/SceneInstaller 와 동일하게 [Referral]/[SceneReferral] 의
        //       BindType 과 Id 를 읽음. 명시적 bindTypeOverride 가 어트리뷰트보다 우선함.
        var compType  = comp.GetType();
        var refAttr   = compType.GetCustomAttribute<ReferralAttribute>();
        var sceneAttr = refAttr == null ? compType.GetCustomAttribute<SceneReferralAttribute>() : null;
        string id     = refAttr?.Id ?? sceneAttr?.Id;
        Type   bind   = bindTypeOverride ?? refAttr?.BindType ?? sceneAttr?.BindType;

        InstallerRegistryHelper.RegisterTypeMappings(comp, _localRegistry, "ObjectInstaller", bind, id);

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
                    _localRegistry, _ownershipMap, owner, "ObjectInstaller"));
        }
    }

    public void Unregister(Type type)
        => Unregister(type, RegistryKey.DefaultId);

    public void Unregister(Type type, string id)
    {
        if (type == null) return;
        _localRegistry.Remove(new RegistryKey(type, id));
    }

    // ─── Resolve 체인 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve 체인: 로컬 레지스트리 → 상위 스코프 순으로 조회.
    ///
    /// 상위 스코프 결정:
    ///   1) _parentScope 가 설정된 경우 — 해당 ObjectInstaller 를 상위로 사용 (재귀적 체인).
    ///   2) _parentScope 가 null 인 경우 — SceneInstaller → MasterInstaller 기본 체인.
    ///
    /// v2.0: id 를 통해 Named 바인딩 조회를 체인 전체에서 전파함.
    /// </summary>
    private Component ResolveInternal(Type type, string id)
    {
        if (type == null) return null;

        var key = new RegistryKey(type, id);

        if (_localRegistry.TryGetValue(key, out var local) && local != null)
            return local;

        if (_parentScope != null)
            return _parentScope.Resolve(type, id);

        if (SceneInstaller.Instance != null)
        {
            var scene = SceneInstaller.Instance.Resolve(type, id);
            if (scene != null) return scene;
        }

        return MasterInstaller.Instance != null ? MasterInstaller.Instance.Resolve(type, id) : null;
    }

    // ── IScope 무키 Resolve (v1.x 하위 호환) ─────────────────────────────────

    public Component Resolve(Type type)
        => ResolveInternal(type, RegistryKey.DefaultId);

    public bool TryResolve(Type type, out Component component)
    {
        component = Resolve(type);
        return component != null;
    }

    public T Resolve<T>() where T : Component
        => Resolve(typeof(T)) as T;

    public T ResolveAs<T>() where T : class
        => Resolve(typeof(T)) as T;

    // ── v2.0: Named Resolve ──────────────────────────────────────────────────

    public Component Resolve(Type type, string id)
        => ResolveInternal(type, id);

    public bool TryResolve(Type type, string id, out Component component)
    {
        component = Resolve(type, id);
        return component != null;
    }

    public T ResolveAs<T>(string id) where T : class
        => Resolve(typeof(T), id) as T;

    // ─── v2.0: 생성자 주입 Create<T> ─────────────────────────────────────────

    /// <summary>
    /// [InjectConstructor] 또는 단일 public 생성자를 통해 T 의 인스턴스를 생성한다.
    /// 생성자 파라미터와 [GlobalInject]/[SceneInject] 필드를 함께 주입함.
    ///
    /// ObjectInstaller 의 Resolve 는 이미 로컬 → 씬 → 전역 전체 체인을 담당하므로
    /// Create&lt;T&gt;() 도 동일한 체인에서 의존성을 해결한다.
    /// </summary>
    public T Create<T>() where T : class
    {
        return InstallerRegistryHelper.CreateAndInject<T>(
            resolver:  (type, id) => ResolveInternal(type, id),
            ownerTag:  "ObjectInstaller",
            onCreated: RegisterTickables);  // v2.1: ITickable 자동 등록
    }

    // ─── v1.2: SpawnInjected ─────────────────────────────────────────────────

    public GameObject SpawnInjected(GameObject prefab)
        => SpawnInjected(prefab, Vector3.zero, Quaternion.identity, null);

    public GameObject SpawnInjected(GameObject prefab, Transform parent)
        => SpawnInjected(prefab, Vector3.zero, Quaternion.identity, parent);

    public GameObject SpawnInjected(
        GameObject prefab,
        Vector3    position,
        Quaternion rotation,
        Transform  parent = null)
    {
        if (prefab == null) return null;

        var instance = parent != null
            ? Instantiate(prefab, position, rotation, parent)
            : Instantiate(prefab, position, rotation);

        InjectGameObject(instance);
        return instance;
    }

    public T SpawnInjected<T>(T prefab) where T : MonoBehaviour
    {
        if (prefab == null) return null;
        var instance = Instantiate(prefab);
        InjectTarget(instance);
        return instance;
    }

    public T SpawnInjected<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        where T : MonoBehaviour
    {
        if (prefab == null) return null;
        var instance = parent != null
            ? Instantiate(prefab, position, rotation, parent)
            : Instantiate(prefab, position, rotation);
        InjectTarget(instance);
        return instance;
    }

    // ─── v1.2: Pool 연동 API ─────────────────────────────────────────────────

    public void InjectTargetFromPool(MonoBehaviour target)
    {
        TryInjectTarget(target, logWarnings: true, isReinjection: true);
    }

    public void ReleaseTargetToPool(MonoBehaviour target)
    {
        if (target == null) return;

        if (target is IPoolInjectionTarget poolTarget)
            poolTarget.OnPoolRelease();

        var type = target.GetType();

        foreach (var field in TypeDataCache.GetGlobalInjectFields(type))
            field.Setter(target, null);

        foreach (var field in TypeDataCache.GetSceneInjectFields(type))
            field.Setter(target, null);
    }

    // ─── Editor (Bake) ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    [ContextMenu("Bake Dependencies")]
    public void BakeDependencies()
    {
        var master = MasterInstaller.Instance;
        if (master != null)
            master.RefreshRegistry();

        var providers = GetComponentsInChildren<Component>(true).ToList();
        var targets   = GetComponentsInChildren<MonoBehaviour>(true);
        int injectCount = 0;

        foreach (var target in targets)
        {
            if (target == null) continue;

            var fields = target.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (System.Attribute.IsDefined(field, typeof(InjectAttribute)))
                {
                    var match = providers.FirstOrDefault(p => p != target && field.FieldType.IsInstanceOfType(p));
                    if (match != null) SetField(target, field, match, ref injectCount);
                }
            }

            UnityEditor.EditorUtility.SetDirty(target);
        }

        Debug.Log($"<color=#42f56c>[ObjectInstaller]</color> Bake Complete! ({injectCount} links updated)");
    }

    private void SetField(object target, FieldInfo field, Component value, ref int count)
    {
        var current = field.GetValue(target) as Component;
        if (current != value)
        {
            UnityEditor.Undo.RecordObject(target as UnityEngine.Object, "Bake Dependency");
            field.SetValue(target, value);
            count++;
        }
    }
#endif
}
