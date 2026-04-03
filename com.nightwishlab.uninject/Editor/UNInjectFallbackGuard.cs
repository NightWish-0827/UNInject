#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play 버튼을 누르기 전에 [GlobalInject] / [SceneInject] 필드를 가졌으나
/// partial 선언이 없어 Roslyn 생성 플랜 대신 Expression Tree 폴백으로 동작할 타입을
/// 탐색하여 경고를 출력함.
///
/// - IL2CPP 빌드에서는 Expression Tree 컴파일이 실패하고
///   FieldInfo.SetValue 폴백까지 내려갈 수 있음.
/// - 이 가드는 그 상황을 개발자가 인지하지 못한 채 배포하는 것을 방지함.
///
/// 관련 진단 코드: UNI001 (Roslyn Source Generator 가 컴파일 타임에 발행)
/// </summary>
[InitializeOnLoad]
public static class UNInjectFallbackGuard
{
    static UNInjectFallbackGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode) return;

        CheckFallbackTypes();
        CheckConstructorFallbackTypes();   // v2.0: [InjectConstructor] 폴백 감지
        CheckRuntimeRegisterCandidates();
    }

    private static void CheckFallbackTypes()
    {
        var fallbackTypes = CollectFallbackTypes();
        if (fallbackTypes.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "[UNInject] The following types are working with Expression Tree fallback instead of Roslyn source generator " +
            "because they don't have a partial declaration.\n" +
            "In IL2CPP builds, FieldInfo.SetValue fallback can also be triggered.\n" +
            "Change to 'public partial class <TypeName>' to remove this warning.\n");

        foreach (var type in fallbackTypes)
            sb.AppendLine($"  • {type.FullName}");

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// [Referral] / [SceneReferral] 어트리뷰트가 붙었으나 partial 선언이 없는 타입을 탐색하여
    /// 런타임 Register() 호출 시 Roslyn 플랜 누락 경고가 발생할 수 있음을 사전에 알린다.
    ///
    /// 배경:
    ///   v1.2 에서 추가된 MasterInstaller.Register(comp) / SceneInstaller.Register(comp) 는
    ///   런타임에 동적으로 컴포넌트를 등록한다. 이때 해당 타입의 Roslyn 생성 플랜이 없으면
    ///   injection 시 Expression Tree 폴백이 사용되어 IL2CPP 에서 문제가 생길 수 있음.
    /// </summary>
    private static void CheckRuntimeRegisterCandidates()
    {
        const BindingFlags Flags = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        var candidates = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsInterface || type.IsAbstract) continue;

                bool isReferral = Attribute.IsDefined(type, typeof(ReferralAttribute))
                               || Attribute.IsDefined(type, typeof(SceneReferralAttribute));
                if (!isReferral) continue;

                bool hasInjectConsumers = false;
                foreach (var field in type.GetFields(Flags))
                {
                    if (field.IsDefined(typeof(GlobalInjectAttribute), false) ||
                        field.IsDefined(typeof(SceneInjectAttribute), false))
                    {
                        hasInjectConsumers = true;
                        break;
                    }
                }

                if (!hasInjectConsumers) continue;

                bool hasPlan = TypeDataCache.HasGeneratedGlobalPlan(type)
                            || TypeDataCache.HasGeneratedScenePlan(type);

                if (!hasPlan)
                    candidates.Add(type);
            }
        }

        if (candidates.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "[UNInject] The following [Referral]/[SceneReferral] types also have inject fields " +
            "but lack a partial declaration.\n" +
            "If these are registered at runtime via Register(), IL2CPP builds may fall back to FieldInfo.SetValue.\n" +
            "Add 'public partial class <TypeName>' to generate a Roslyn plan.\n");

        foreach (var t in candidates)
            sb.AppendLine($"  • {t.FullName}");

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// v2.0: [InjectConstructor] 가 붙은 생성자를 가졌으나
    /// Roslyn Generator 팩토리(HasGeneratedFactory)가 등록되지 않은 타입을 탐색하여 경고함.
    ///
    /// partial 선언이 없거나 Generator DLL 이 참조되지 않은 경우 reflection 폴백
    /// (ConstructorInfo.Invoke) 이 발생하며, IL2CPP 빌드에서 해당 타입이 스트리핑될 수 있음.
    ///
    /// 관련 진단 코드: UNI002 (Roslyn Source Generator 가 컴파일 타임에 발행)
    /// </summary>
    private static void CheckConstructorFallbackTypes()
    {
        const BindingFlags CtorFlags = BindingFlags.Public | BindingFlags.Instance;
        var fallbacks = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsInterface || type.IsAbstract) continue;

                bool hasInjectConstructor = false;
                foreach (var ctor in type.GetConstructors(CtorFlags))
                {
                    if (ctor.IsDefined(typeof(InjectConstructorAttribute), false))
                    {
                        hasInjectConstructor = true;
                        break;
                    }
                }

                if (!hasInjectConstructor) continue;
                if (!TypeDataCache.HasGeneratedFactory(type))
                    fallbacks.Add(type);
            }
        }

        if (fallbacks.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "[UNInject] The following types have [InjectConstructor] but no Roslyn factory plan (HasGeneratedFactory = false).\n" +
            "IScope.Create<T>() will fall back to ConstructorInfo.Invoke (reflection).\n" +
            "In IL2CPP builds, the constructor and its parameters may be stripped unless preserved in link.xml.\n" +
            "Change to 'public partial class <TypeName>' to enable factory code generation (UNI002).\n");

        bool hasTickable = false;
        foreach (var type in fallbacks)
        {
            // v2.1: ITickable 구현 여부를 함께 표시
            string tickHint = string.Empty;
            if (typeof(ITickable).IsAssignableFrom(type))       tickHint += " [ITickable]";
            if (typeof(IFixedTickable).IsAssignableFrom(type))  tickHint += " [IFixedTickable]";
            if (typeof(ILateTickable).IsAssignableFrom(type))   tickHint += " [ILateTickable]";
            if (tickHint.Length > 0) hasTickable = true;

            sb.AppendLine($"  • {type.FullName}{tickHint}");
        }

        if (hasTickable)
            sb.AppendLine(
                "\n[UNInject] ⚠ Types marked with [ITickable*] above will attempt auto-registration via Create<T>(), " +
                "but in IL2CPP builds, a stripped constructor will cause Create<T>() to throw — " +
                "preventing Tick() from ever being registered. " +
                "Add 'public partial class' to generate a safe factory plan.");

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// 현재 로드된 어셈블리에서 [GlobalInject] 또는 [SceneInject] 필드를 가졌으나
    /// TypeDataCache 에 생성 플랜이 등록되지 않은 타입을 수집함.
    /// </summary>
    private static List<Type> CollectFallbackTypes()
    {
        const BindingFlags Flags = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsInterface || type.IsAbstract) continue;

                bool hasInjectField = false;

                foreach (var field in type.GetFields(Flags))
                {
                    if (field.IsDefined(typeof(GlobalInjectAttribute), false) ||
                        field.IsDefined(typeof(SceneInjectAttribute), false))
                    {
                        hasInjectField = true;
                        break;
                    }
                }

                if (!hasInjectField) continue;

                // 생성 플랜이 없으면 폴백 타입으로 분류
                bool hasPlan = TypeDataCache.HasGeneratedGlobalPlan(type)
                               || TypeDataCache.HasGeneratedScenePlan(type);

                if (!hasPlan)
                    result.Add(type);
            }
        }

        return result;
    }

    private static bool ShouldSkipAssembly(string name)
        => UNInjectEditorUtility.ShouldSkipAssembly(name);
}
#endif
