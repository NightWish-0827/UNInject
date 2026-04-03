#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 빌드 전에 BakeUp 된 레지스트리와 [GlobalInject]/[SceneInject] 필드 선언을
/// 교차 검증하여 누락된 등록을 보고하는 빌드 전처리기.
///
/// 동작 방식:
///   1) 로드된 어셈블리에서 [GlobalInject(optional: false)]/[SceneInject(optional: false)] 필드를 수집.
///   2) EditorBuildSettings.scenes 의 각 씬을 열어 MasterInstaller._globalReferrals 와
///      SceneInstaller._sceneReferrals 직렬화 필드를 읽어 커버리지를 확인.
///   3) 누락된 타입이 있으면 Debug.LogError 로 보고. 빌드를 중단하려면
///      UNINJECT_STRICT_BUILD 스크립팅 심볼을 추가하면 BuildFailedException 을 던짐.
///
/// 메뉴:
///   Window > UNInject > Validate Bake — 빌드 없이 즉시 검증 실행.
/// </summary>
public class UNInjectBakeValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        var errors = RunValidation();

        if (errors.Count == 0)
        {
            Debug.Log("<color=#42f56c>[UNInjectBakeValidator]</color> Bake validation passed. No missing registrations.");
            return;
        }

        foreach (var err in errors)
            Debug.LogError(err);

#if UNINJECT_STRICT_BUILD
        throw new BuildFailedException(
            $"[UNInjectBakeValidator] Build aborted: {errors.Count} missing registration(s). " +
            "Check the Console for details.");
#else
        Debug.LogWarning(
            $"<color=yellow>[UNInjectBakeValidator]</color> {errors.Count} missing registration(s) found. " +
            "Add 'UNINJECT_STRICT_BUILD' scripting symbol to fail the build on validation errors.");
#endif
    }

    // ─── 메뉴 진입점 ──────────────────────────────────────────────────────────

    [MenuItem("Window/UNInject/Validate Bake")]
    public static void RunFromMenu()
    {
        var errors = RunValidation();

        if (errors.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "UNInject Bake Validation",
                "All validations passed! No missing registrations found.",
                "OK");
        }
        else
        {
            var body = string.Join("\n", errors);
            EditorUtility.DisplayDialog(
                "UNInject Bake Validation",
                $"{errors.Count} error(s) found:\n\n{body}",
                "OK");

            foreach (var err in errors)
                Debug.LogError(err);
        }
    }

    // ─── 핵심 검증 로직 ───────────────────────────────────────────────────────

    public static List<string> RunValidation()
    {
        var errors = new List<string>();

        // 무키(unkeyed) 검증
        var requiredGlobal = CollectRequiredInjectTypes(typeof(GlobalInjectAttribute));
        var requiredScene  = CollectRequiredInjectTypes(typeof(SceneInjectAttribute));

        // v2.0: Named 바인딩 검증
        var requiredNamedGlobal = CollectNamedInjectTypes(typeof(GlobalInjectAttribute));
        var requiredNamedScene  = CollectNamedInjectTypes(typeof(SceneInjectAttribute));

        if (requiredGlobal.Count == 0 && requiredScene.Count == 0 &&
            requiredNamedGlobal.Count == 0 && requiredNamedScene.Count == 0)
            return errors;

        var coveredGlobal      = new HashSet<Type>();
        var coveredScene       = new HashSet<Type>();
        var coveredNamedGlobal = new HashSet<RegistryKey>();
        var coveredNamedScene  = new HashSet<RegistryKey>();

        var activeScene = EditorSceneManager.GetActiveScene();

        foreach (var sceneRef in EditorBuildSettings.scenes)
        {
            if (!sceneRef.enabled) continue;

            bool isAlreadyOpen = activeScene.path == sceneRef.path && activeScene.isLoaded;
            var openedScene = isAlreadyOpen
                ? activeScene
                : EditorSceneManager.OpenScene(sceneRef.path, OpenSceneMode.Additive);

            try
            {
                CollectCoveredTypes(coveredGlobal, coveredScene, coveredNamedGlobal, coveredNamedScene);
            }
            finally
            {
                if (!isAlreadyOpen && openedScene.isLoaded)
                    EditorSceneManager.CloseScene(openedScene, removeScene: true);
            }
        }

        // ── 무키 검증 결과 ────────────────────────────────────────────────────
        foreach (var kvp in requiredGlobal)
        {
            if (!coveredGlobal.Contains(kvp.Key))
            {
                errors.Add(
                    $"[UNInject] Missing [Referral] for type '{kvp.Key.Name}' " +
                    $"— required by [GlobalInject] field in '{kvp.Value.Name}'. " +
                    "Run 'Refresh Global Registry' on MasterInstaller.");
            }
        }

        foreach (var kvp in requiredScene)
        {
            if (!coveredScene.Contains(kvp.Key))
            {
                errors.Add(
                    $"[UNInject] Missing [SceneReferral] for type '{kvp.Key.Name}' " +
                    $"— required by [SceneInject] field in '{kvp.Value.Name}'. " +
                    "Run 'Refresh Scene Registry' on SceneInstaller.");
            }
        }

        // ── v2.0: Named 바인딩 검증 결과 ──────────────────────────────────────
        foreach (var kvp in requiredNamedGlobal)
        {
            if (!coveredNamedGlobal.Contains(kvp.Key))
            {
                errors.Add(
                    $"[UNInject] Missing [Referral(id:\"{kvp.Key.Id}\")] for type '{kvp.Key.Type.Name}' " +
                    $"— required by [GlobalInject(\"{kvp.Key.Id}\")] field in '{kvp.Value.Name}'. " +
                    "Ensure a component with the matching [Referral(id)] is registered in MasterInstaller.");
            }
        }

        foreach (var kvp in requiredNamedScene)
        {
            if (!coveredNamedScene.Contains(kvp.Key))
            {
                errors.Add(
                    $"[UNInject] Missing [SceneReferral(id:\"{kvp.Key.Id}\")] for type '{kvp.Key.Type.Name}' " +
                    $"— required by [SceneInject(\"{kvp.Key.Id}\")] field in '{kvp.Value.Name}'. " +
                    "Ensure a component with the matching [SceneReferral(id)] is registered in SceneInstaller.");
            }
        }

        return errors;
    }

    /// <summary>
    /// 현재 로드된 씬에서 MasterInstaller._globalReferrals 와
    /// SceneInstaller._sceneReferrals 를 읽어 무키/Named 커버된 타입 집합을 모두 채운다.
    /// </summary>
    private static void CollectCoveredTypes(
        HashSet<Type>        coveredGlobal,
        HashSet<Type>        coveredScene,
        HashSet<RegistryKey> coveredNamedGlobal,
        HashSet<RegistryKey> coveredNamedScene)
    {
        var masterInstaller = UnityEngine.Object.FindObjectOfType<MasterInstaller>();
        if (masterInstaller != null)
        {
            var so = new SerializedObject(masterInstaller);
            AddReferralTypes(so.FindProperty("_globalReferrals"),
                             coveredGlobal, coveredNamedGlobal, isGlobal: true);
        }

        var sceneInstaller = UnityEngine.Object.FindObjectOfType<SceneInstaller>();
        if (sceneInstaller != null)
        {
            var so = new SerializedObject(sceneInstaller);
            AddReferralTypes(so.FindProperty("_sceneReferrals"),
                             coveredScene, coveredNamedScene, isGlobal: false);
        }
    }

    /// <summary>
    /// 직렬화된 Referral 배열을 읽어 무키 커버 집합(covered)과
    /// Named 커버 집합(coveredNamed)을 동시에 채운다.
    ///
    /// isGlobal = true  → [ReferralAttribute] 에서 Id 를 읽음.
    /// isGlobal = false → [SceneReferralAttribute] 에서 Id 를 읽음.
    /// </summary>
    private static void AddReferralTypes(
        SerializedProperty   arrayProp,
        HashSet<Type>        covered,
        HashSet<RegistryKey> coveredNamed,
        bool                 isGlobal)
    {
        if (arrayProp == null) return;

        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            var elem = arrayProp.GetArrayElementAtIndex(i);
            if (elem.objectReferenceValue is not Component comp) continue;

            var concreteType = comp.GetType();

            // v2.0: 컴포넌트 타입에 붙은 [Referral] / [SceneReferral] 의 Id 추출
            string id = isGlobal
                ? (concreteType.GetCustomAttribute<ReferralAttribute>()?.Id      ?? string.Empty)
                : (concreteType.GetCustomAttribute<SceneReferralAttribute>()?.Id ?? string.Empty);

            covered.Add(concreteType);
            coveredNamed.Add(new RegistryKey(concreteType, id));

            foreach (var itf in concreteType.GetInterfaces())
            {
                if (InstallerRegistryHelper.IsMappableAbstraction(itf))
                {
                    covered.Add(itf);
                    coveredNamed.Add(new RegistryKey(itf, id));
                }
            }

            var baseType = concreteType.BaseType;
            while (baseType != null &&
                   baseType != typeof(object)       &&
                   baseType != typeof(Component)    &&
                   baseType != typeof(Behaviour)    &&
                   baseType != typeof(MonoBehaviour))
            {
                covered.Add(baseType);
                coveredNamed.Add(new RegistryKey(baseType, id));
                baseType = baseType.BaseType;
            }
        }
    }

    /// <summary>
    /// 로드된 어셈블리에서 특정 Inject 어트리뷰트로 마킹된 non-optional 무키(unkeyed) 필드의
    /// 필드 타입 → 선언 타입 매핑을 수집한다.
    ///
    /// v2.0: Named 바인딩 필드(Id != string.Empty)는 이 검증에서 제외한다.
    ///   Named 필드는 CollectNamedInjectTypes 에서 별도로 수집하여 검증함.
    /// </summary>
    private static Dictionary<Type, Type> CollectRequiredInjectTypes(Type attributeType)
    {
        var result = new Dictionary<Type, Type>();

        const BindingFlags Flags = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;

                var current = type;
                while (current != null && current != typeof(object))
                {
                    foreach (var field in current.GetFields(Flags))
                    {
                        var attr = field.GetCustomAttribute(attributeType);
                        if (attr == null) continue;

                        bool optional = attr is GlobalInjectAttribute g ? g.Optional
                                      : attr is SceneInjectAttribute  s ? s.Optional
                                      : false;

                        // v2.0: Named 바인딩 필드는 무키 검증에서 제외 (오탐 방지)
                        string id = attr is GlobalInjectAttribute gi ? gi.Id
                                  : attr is SceneInjectAttribute  si ? si.Id
                                  : string.Empty;

                        if (!optional && string.IsNullOrEmpty(id) && !result.ContainsKey(field.FieldType))
                            result[field.FieldType] = type;
                    }
                    current = current.BaseType;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 로드된 어셈블리에서 특정 Inject 어트리뷰트로 마킹된 non-optional Named 필드의
    /// RegistryKey(FieldType, Id) → 선언 타입 매핑을 수집한다.
    ///
    /// Id 가 비어 있는 무키 필드는 포함하지 않는다.
    /// </summary>
    private static Dictionary<RegistryKey, Type> CollectNamedInjectTypes(Type attributeType)
    {
        var result = new Dictionary<RegistryKey, Type>();

        const BindingFlags Flags = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;

                var current = type;
                while (current != null && current != typeof(object))
                {
                    foreach (var field in current.GetFields(Flags))
                    {
                        var attr = field.GetCustomAttribute(attributeType);
                        if (attr == null) continue;

                        bool optional = attr is GlobalInjectAttribute g ? g.Optional
                                      : attr is SceneInjectAttribute  s ? s.Optional
                                      : false;

                        string id = attr is GlobalInjectAttribute gi ? gi.Id
                                  : attr is SceneInjectAttribute  si ? si.Id
                                  : string.Empty;

                        if (!optional && !string.IsNullOrEmpty(id))
                        {
                            var key = new RegistryKey(field.FieldType, id);
                            if (!result.ContainsKey(key))
                                result[key] = type;
                        }
                    }
                    current = current.BaseType;
                }
            }
        }

        return result;
    }

    private static bool ShouldSkipAssembly(string name)
        => UNInjectEditorUtility.ShouldSkipAssembly(name);
}
#endif
