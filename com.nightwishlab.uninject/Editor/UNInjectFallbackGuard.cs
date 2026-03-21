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
    {
        return name.StartsWith("System")       || name.StartsWith("Microsoft")
            || name.StartsWith("UnityEngine")  || name.StartsWith("UnityEditor")
            || name.StartsWith("Unity.")       || name.StartsWith("mscorlib")
            || name.StartsWith("netstandard")  || name.StartsWith("Mono.")
            || name.StartsWith("nunit.")       || name.StartsWith("JetBrains.")
            || name.StartsWith("ExCSS.")       || name.StartsWith("Bee.");
    }
}
#endif
