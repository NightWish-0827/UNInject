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
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class GlobalInjectAttribute : Attribute
{
    /// <summary>
    /// true면 해당 의존성이 없어도 경고/실패로 취급하지 않음.
    /// (엔터프라이즈 운영 환경에서 "선택 의존성"을 명시하기 위한 옵션)
    /// </summary>
    public bool Optional { get; private set; }

    public GlobalInjectAttribute() { }
    public GlobalInjectAttribute(bool optional)
    {
        Optional = optional;
    }
}

/// <summary>
/// [Scene]
/// - SceneInstaller 가 인덱싱한 씬 전용 Manager Layer 컴포넌트 중
///   타입이 일치하는 대상을 런타임에 주입받기 위한 마커.
/// - [SerializeField] 를 사용하지 않는 private 필드에 붙여 사용함.
/// - 실제 값 설정은 ObjectInstaller.Awake 에서 SceneInstaller.Resolve 를 통해 이루어짐.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SceneInjectAttribute : Attribute
{
    /// <summary>
    /// true면 해당 의존성이 없어도 경고/실패로 취급하지 않음.
    /// (엔터프라이즈 운영 환경에서 "선택 의존성"을 명시하기 위한 옵션)
    /// </summary>
    public bool Optional { get; private set; }

    public SceneInjectAttribute() { }
    public SceneInjectAttribute(bool optional)
    {
        Optional = optional;
    }
}

/// <summary>
/// 주입이 완료된 시점에 후처리를 수행하고 싶은 컴포넌트를 위한 콜백 인터페이스.
/// - ObjectInstaller.TryInjectTarget()가 "필수 의존성" 주입에 성공했을 때 호출됨.
/// - Optional 의존성 누락은 성공/실패에 영향을 주지 않음.
/// </summary>
public interface IInjected
{
    void OnInjected();
}

/// <summary>
/// 이 Attribute 가 붙은 MonoBehaviour 는
/// MasterInstaller 가 관리하는 전역 Manager Layer 레지스트리에 포함됨.
/// (게임 전체에서 공유되는 전역 매니저)
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ReferralAttribute : Attribute
{
    /// <summary>
    /// 이 컴포넌트를 어떤 타입(주로 인터페이스)으로 바인딩할지 지정함.
    /// 지정되면 MasterInstaller 레지스트리는 구체 타입 대신 BindType 을 Key 로 사용함.
    /// </summary>
    public Type BindType { get; private set; }

    public ReferralAttribute() { }
    public ReferralAttribute(Type bindType)
    {
        BindType = bindType;
    }
}

/// <summary>
/// 이 Attribute 가 붙은 MonoBehaviour 는
/// SceneInstaller 가 관리하는 씬 전용 Manager Layer 레지스트리에 포함됨.
/// (해당 씬 안에서만 의미가 있는 매니저)
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SceneReferralAttribute : Attribute
{
    /// <summary>
    /// 이 컴포넌트를 어떤 타입(주로 인터페이스)으로 바인딩할지 지정함.
    /// 지정되면 SceneInstaller 레지스트리는 구체 타입 대신 BindType 을 Key 로 사용함.
    /// </summary>
    public Type BindType { get; private set; }

    public SceneReferralAttribute() { }
    public SceneReferralAttribute(Type bindType)
    {
        BindType = bindType;
    }
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
    // 일단은 ReadOnly 로 표시
    // 추후에 ReadOnly 표기 말고 좋은 렌더링 방식이 생각나면 변경 예정임.
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