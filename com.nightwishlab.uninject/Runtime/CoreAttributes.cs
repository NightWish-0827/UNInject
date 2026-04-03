using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ------------------- Attributes -------------------

/// <summary>
/// [Local]
/// - 같은 ObjectInstaller 루트 계층 안에서
///   타입이 일치하는 컴포넌트를 찾아 에디터에서 베이크하는 필드.
/// - 반드시 [SerializeField] 와 함께 사용하고,
///   값은 ObjectInstaller.BakeDependencies 에 의해 자동 설정됨.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class InjectAttribute : PropertyAttribute { }

/// <summary>
/// [Global]
/// - MasterInstaller 가 인덱싱한 Manager Layer 컴포넌트 중
///   타입이 일치하는 대상을 런타임에 주입받기 위한 마커.
/// - [SerializeField] 를 사용하지 않는 private 필드에 붙여 사용함.
/// - 실제 값 설정은 ObjectInstaller.Awake 에서 MasterInstaller.Resolve 를 통해 이루어짐.
///
/// [v2.0] Id 를 지정하면 동일 타입의 Named 바인딩에서 원하는 인스턴스를 선택할 수 있음.
///   예) [GlobalInject("enemyA")] private IEnemyManager _enemyA;
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
public class GlobalInjectAttribute : Attribute
{
    /// <summary>true면 해당 의존성이 없어도 경고/실패로 취급하지 않음.</summary>
    public bool Optional { get; private set; }

    /// <summary>
    /// v2.0: Named 바인딩 키. string.Empty 이면 무키(기본) 바인딩과 동일.
    /// [Referral(id)] 와 쌍을 이루어야 함.
    /// </summary>
    public string Id { get; private set; }

    public GlobalInjectAttribute()                           { Id = string.Empty; }
    public GlobalInjectAttribute(bool optional)              { Optional = optional; Id = string.Empty; }
    public GlobalInjectAttribute(string id)                  { Id = id ?? string.Empty; }
    public GlobalInjectAttribute(string id, bool optional)   { Id = id ?? string.Empty; Optional = optional; }
}

/// <summary>
/// [Scene]
/// - SceneInstaller 가 인덱싱한 씬 전용 Manager Layer 컴포넌트 중
///   타입이 일치하는 대상을 런타임에 주입받기 위한 마커.
/// - [SerializeField] 를 사용하지 않는 private 필드에 붙여 사용함.
/// - 실제 값 설정은 ObjectInstaller.Awake 에서 SceneInstaller.Resolve 를 통해 이루어짐.
///
/// [v2.0] Id 를 지정하면 동일 타입의 Named 바인딩에서 원하는 인스턴스를 선택할 수 있음.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
public class SceneInjectAttribute : Attribute
{
    /// <summary>true면 해당 의존성이 없어도 경고/실패로 취급하지 않음.</summary>
    public bool Optional { get; private set; }

    /// <summary>v2.0: Named 바인딩 키. string.Empty 이면 무키 바인딩.</summary>
    public string Id { get; private set; }

    public SceneInjectAttribute()                          { Id = string.Empty; }
    public SceneInjectAttribute(bool optional)             { Optional = optional; Id = string.Empty; }
    public SceneInjectAttribute(string id)                 { Id = id ?? string.Empty; }
    public SceneInjectAttribute(string id, bool optional)  { Id = id ?? string.Empty; Optional = optional; }
}

/// <summary>
/// 주입이 완료된 시점에 후처리를 수행하고 싶은 컴포넌트를 위한 콜백 인터페이스.
/// ObjectInstaller.TryInjectTarget() 또는 IScope.Create() 가 필수 의존성 주입에 성공했을 때 호출됨.
/// Optional 의존성 누락은 성공/실패에 영향을 주지 않음.
/// </summary>
public interface IInjected
{
    void OnInjected();
}

/// <summary>
/// 이 Attribute 가 붙은 MonoBehaviour 는
/// MasterInstaller 가 관리하는 전역 Manager Layer 레지스트리에 포함됨.
/// (게임 전체에서 공유되는 전역 매니저)
///
/// [v2.0] Id 를 지정하면 같은 BindType 의 Named 바인딩으로 등록됨.
///   예) [Referral("enemyA", typeof(IEnemyManager))]
///       → RegistryKey(typeof(IEnemyManager), "enemyA") 로 등록
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ReferralAttribute : Attribute
{
    /// <summary>이 컴포넌트를 어떤 타입(주로 인터페이스)으로 바인딩할지 지정함.</summary>
    public Type   BindType { get; private set; }

    /// <summary>v2.0: Named 바인딩 키. string.Empty 이면 무키 바인딩.</summary>
    public string Id       { get; private set; }

    public ReferralAttribute()                             { Id = string.Empty; }
    public ReferralAttribute(Type bindType)                { BindType = bindType; Id = string.Empty; }
    public ReferralAttribute(string id)                    { Id = id ?? string.Empty; }
    public ReferralAttribute(Type bindType, string id)     { BindType = bindType; Id = id ?? string.Empty; }
}

/// <summary>
/// 이 Attribute 가 붙은 MonoBehaviour 는
/// SceneInstaller 가 관리하는 씬 전용 Manager Layer 레지스트리에 포함됨.
/// (해당 씬 안에서만 의미가 있는 매니저)
///
/// [v2.0] Id 지정으로 Named 바인딩 지원.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SceneReferralAttribute : Attribute
{
    /// <summary>이 컴포넌트를 어떤 타입(주로 인터페이스)으로 바인딩할지 지정함.</summary>
    public Type   BindType { get; private set; }

    /// <summary>v2.0: Named 바인딩 키. string.Empty 이면 무키 바인딩.</summary>
    public string Id       { get; private set; }

    public SceneReferralAttribute()                            { Id = string.Empty; }
    public SceneReferralAttribute(Type bindType)               { BindType = bindType; Id = string.Empty; }
    public SceneReferralAttribute(string id)                   { Id = id ?? string.Empty; }
    public SceneReferralAttribute(Type bindType, string id)    { BindType = bindType; Id = id ?? string.Empty; }
}

/// <summary>
/// [v2.0] 생성자 주입(Constructor Injection) 대상 생성자를 지정하는 마커.
///
/// IScope.Create&lt;T&gt;() 호출 시 이 어트리뷰트가 붙은 생성자를 통해 인스턴스를 생성함.
/// 이 어트리뷰트가 없으면 public 생성자가 단 하나인 경우에 한해 자동 선택됨.
///
/// 주요 대상: ScriptableObject, 순수 C# 서비스 클래스.
/// MonoBehaviour 의 생성자에는 사용하지 않음.
///
/// 생성자 파라미터에 [GlobalInject] / [SceneInject] 를 붙여 Named 바인딩도 지원함.
///   예)
///   [InjectConstructor]
///   public MyService([GlobalInject("audio")] IAudioManager audio, IInputManager input) { ... }
///
/// ※ IL2CPP 빌드: 리플렉션 기반 생성자 호출을 사용하므로 관련 타입이 코드 스트리핑되지
///   않도록 link.xml 에 보존 규칙을 추가하거나, Roslyn Generator 확장을 기다릴 것.
///   Roslyn 확장 필요: UNInjectGenerator 가 [InjectConstructor] 가 붙은 타입에 대해
///   non-reflective 생성 플랜을 생성해야 함.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor)]
public class InjectConstructorAttribute : Attribute { }


// ------------------- Scope / Lifetime -------------------

/// <summary>
/// MasterInstaller / SceneInstaller / ObjectInstaller 가 공통으로 구현하는 스코프 계약.
/// 런타임 Register/Unregister API 와 Resolve 체인을 통일된 인터페이스로 제공함.
///
/// [v2.0] Named 바인딩 지원:
///   - Resolve(Type, string id) / ResolveAs&lt;T&gt;(string id) 오버로드 추가.
///   - Unregister(Type, string id) 오버로드 추가.
///   - Create&lt;T&gt;(): 생성자 주입을 통한 순수 C# 인스턴스 생성.
/// </summary>
public interface IScope
{
    /// <summary>컴포넌트를 스코프에 런타임 등록한다. 소유권 없이 등록.</summary>
    void Register(Component comp);

    /// <summary>컴포넌트를 소유자와 함께 등록한다. 소유자가 파괴되면 자동으로 언레지스터됨.</summary>
    void Register(Component comp, MonoBehaviour owner);

    /// <summary>지정 타입의 무키(기본) 등록을 스코프에서 제거한다.</summary>
    void Unregister(Type type);

    /// <summary>v2.0: 지정 타입 + Id 의 Named 등록을 스코프에서 제거한다.</summary>
    void Unregister(Type type, string id);

    // ── 무키 Resolve (v1.x 하위 호환) ─────────────────────────────────────

    Component Resolve(Type type);
    bool TryResolve(Type type, out Component component);
    T Resolve<T>() where T : Component;
    T ResolveAs<T>() where T : class;

    // ── v2.0: Named Resolve ────────────────────────────────────────────────

    /// <summary>지정 타입 + Id 로 Named 등록된 컴포넌트를 반환한다.</summary>
    Component Resolve(Type type, string id);

    /// <summary>지정 타입 + Id 로 Named 등록된 컴포넌트를 반환하며 성공 여부를 반환한다.</summary>
    bool TryResolve(Type type, string id, out Component component);

    /// <summary>인터페이스/추상 타입 + Id 로 Named 등록된 컴포넌트를 반환한다.</summary>
    T ResolveAs<T>(string id) where T : class;

    // ── v2.0: 생성자 주입 ──────────────────────────────────────────────────

    /// <summary>
    /// [InjectConstructor] 또는 단일 public 생성자를 통해 T 의 인스턴스를 생성하고,
    /// [GlobalInject] / [SceneInject] 필드도 함께 주입한다.
    ///
    /// T 가 ITickable / IFixedTickable / ILateTickable 을 구현하면
    /// 해당 스코프의 Update / FixedUpdate / LateUpdate 에 자동 등록된다.
    ///
    /// 대상: ScriptableObject, 순수 C# 서비스 클래스.
    /// MonoBehaviour 에는 사용하지 않는다.
    /// </summary>
    T Create<T>() where T : class;

    // ── v2.1: Tickable 개별 해제 ────────────────────────────────────────────

    /// <summary>
    /// 이 스코프의 Update 틱 목록에서 해당 Tickable 을 제거한다.
    /// 스코프 파괴 시에는 자동 해제되므로 명시적 호출이 필수는 아님.
    /// </summary>
    void UnregisterTickable(ITickable tickable);

    /// <summary>이 스코프의 FixedUpdate 틱 목록에서 해당 Tickable 을 제거한다.</summary>
    void UnregisterTickable(IFixedTickable tickable);

    /// <summary>이 스코프의 LateUpdate 틱 목록에서 해당 Tickable 을 제거한다.</summary>
    void UnregisterTickable(ILateTickable tickable);
}

/// <summary>
/// 스코프에서 언레지스터될 때 후처리를 수행하기 위한 콜백 인터페이스.
/// IInjected 의 언레지스터 대응 쌍.
/// </summary>
public interface IUnregistered
{
    void OnUnregistered();
}

// ─── v2.1: Tickable 생명주기 인터페이스 ─────────────────────────────────────────
//
// IScope.Create<T>() 로 생성된 순수 C# 서비스가 Unity 생명주기에 참여하는 수단.
// 인터페이스를 구현하면 생성 시 해당 스코프의 Installer(MonoBehaviour) 가
// 자신의 Update / FixedUpdate / LateUpdate 에서 각 메서드를 위임 호출한다.
//
// 설계 원칙:
//   - PlayerLoop 직접 조작 없이 MonoBehaviour 위임으로 구현 → Unity 버전에 무관.
//   - 소유 스코프가 파괴될 때 Tickable 도 자동으로 해제됨 (스코프 소유권 모델).
//   - 등록 스코프에서만 틱을 받음; 부모 스코프로 전파되지 않음.
//   - Tick() 실행 중 UnregisterTickable() 호출은 다음 프레임부터 반영됨 (스냅샷 패턴).
//
// 사용 예:
//   public partial class EnemyAIService : ITickable
//   {
//       [InjectConstructor]
//       public EnemyAIService(IEnemyManager em) { ... }
//
//       public void Tick() { /* 매 Update 마다 호출 */ }
//   }
//   var ai = sceneInstaller.Create<EnemyAIService>();   // 자동으로 Tick 등록됨

/// <summary>
/// IScope.Create&lt;T&gt;() 로 생성된 서비스의 매 Update 틱 수신 계약.
/// </summary>
public interface ITickable
{
    void Tick();
}

/// <summary>
/// IScope.Create&lt;T&gt;() 로 생성된 서비스의 매 FixedUpdate 틱 수신 계약.
/// </summary>
public interface IFixedTickable
{
    void FixedTick();
}

/// <summary>
/// IScope.Create&lt;T&gt;() 로 생성된 서비스의 매 LateUpdate 틱 수신 계약.
/// </summary>
public interface ILateTickable
{
    void LateTick();
}

// ─── IScopeDestroyable ─────────────────────────────────────────────────────
//
// IScope.Create<T>() 로 생성된 순수 C# 서비스는 MonoBehaviour.OnDestroy() 콜백을
// 직접 받을 수 없다. IScopeDestroyable 은 이 서비스가 소유 스코프의 파괴 시점을
// 구독할 수 있게 해주는 계약이다.
//
// 호출 시점:
//   - 소유 스코프(MasterInstaller / SceneInstaller / ObjectInstaller)의 OnDestroy() 에서
//     TickableRegistry.ClearWithDestroy() 를 통해 일괄 호출됨.
//   - ITickable, IFixedTickable, ILateTickable 해제보다 먼저 실행되며,
//     예외가 발생해도 다른 서비스의 OnScopeDestroy() 는 계속 호출됨.
//
// 사용 예:
//   public partial class AnalyticsService : IScopeDestroyable
//   {
//       public void OnScopeDestroy() { /* 이벤트 구독 해제, 내부 자원 정리 */ }
//   }

/// <summary>
/// IScope.Create&lt;T&gt;() 로 생성된 서비스의 소유 스코프 파괴 시 정리 콜백.
///
/// IInjected.OnInjected()        — 주입 완료 직후
/// IScopeDestroyable.OnScopeDestroy() — 스코프 OnDestroy 직전
///
/// MonoBehaviour 에는 적용되지 않는다.
/// </summary>
public interface IScopeDestroyable
{
    void OnScopeDestroy();
}

/// <summary>
/// 오브젝트 풀 사이클과 주입을 연동하기 위한 인터페이스.
/// ObjectInstaller.InjectTargetFromPool() 호출 시 OnPoolGet() 이 실행됨.
/// ObjectInstaller.ReleaseTargetToPool()  호출 시 OnPoolRelease() 가 실행되고
/// [GlobalInject]/[SceneInject] 필드가 null 로 초기화됨.
/// </summary>
public interface IPoolInjectionTarget
{
    /// <summary>풀에서 꺼낼 때 호출됨. 재주입 완료 직후 실행.</summary>
    void OnPoolGet();

    /// <summary>풀에 반환할 때 호출됨. 필드 초기화 직전 실행.</summary>
    void OnPoolRelease();
}

/// <summary>
/// SceneInstaller 가 OnDestroy 될 때 레지스트리를 어떻게 처리할지 결정하는 정책.
/// Additive 씬 로딩 등에서 씬 전환 후에도 레지스트리를 유지해야 할 때 Preserve 를 사용함.
/// </summary>
public enum SceneExitPolicy
{
    /// <summary>OnDestroy 시 레지스트리를 즉시 초기화. (기본값)</summary>
    Clear,

    /// <summary>OnDestroy 후에도 레지스트리를 메모리에 유지.
    /// 다음 SceneInstaller 가 초기화될 때까지 기존 씬 의존성을 제공함.</summary>
    Preserve,
}

// ------------------- Editor Drawers -------------------
#if UNITY_EDITOR

/// <summary>
/// [Inject] 필드를 인스펙터에서 읽기 전용으로 표시하는 Drawer.
/// (GlobalInject 는 런타임 주입 전용이므로 인스펙터에 노출하지 않음)
/// </summary>
[CustomPropertyDrawer(typeof(InjectAttribute))]
public class InjectDrawer : PropertyDrawer
{
    // true면 아예 숨김, false면 'Read Only'로 보여줌 (디버깅용)
    private const bool HIDE_COMPLETELY = false;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
#pragma warning disable CS0162
        if (HIDE_COMPLETELY) return;
#pragma warning restore CS0162

        GUI.enabled = false;

        string prefix = "[Local]";
        label.text = $"{prefix} {label.text}";
        EditorGUI.PropertyField(position, property, label);
        GUI.enabled = true;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return HIDE_COMPLETELY ? 0f : base.GetPropertyHeight(property, label);
    }
}
#endif
