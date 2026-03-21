#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

[CustomEditor(typeof(ObjectInstaller))]
public class ObjectInstallerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ObjectInstaller installer = (ObjectInstaller)target;

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("🍩 Bake Dependencies", GUILayout.Height(20)))
        {
            installer.BakeDependencies();
        }
        GUI.backgroundColor = Color.white;

        DrawReferenceOverview(installer);

        EditorGUILayout.Space(8);

        serializedObject.Update();
        var prop = serializedObject.GetIterator();
        bool enterChildren = true;

        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (prop.name == "m_Script")
                continue;

            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawReferenceOverview(ObjectInstaller installer)
    {
        var monoBehaviours = installer.GetComponentsInChildren<MonoBehaviour>(true);

        int injectFieldCount = 0;
        int injectWiredCount = 0;
        int sceneInjectFieldCount = 0;
        int globalInjectFieldCount = 0;

        var localLines = new List<(bool linked, string text)>();
        var sceneLines = new List<(bool inSceneRegistry, bool optional, string text)>();
        var globalLines = new List<(bool inRegistry, bool optional, string text)>();

        foreach (var mb in monoBehaviours)
        {
            if (mb == null) continue;

            var fields = mb.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var so = new SerializedObject(mb);

            foreach (var field in fields)
            {
                if (System.Attribute.IsDefined(field, typeof(InjectAttribute)))
                {
                    injectFieldCount++;

                    Object value = null;
                    var sp = so.FindProperty(field.Name);
                    if (sp != null && sp.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        value = sp.objectReferenceValue;
                    }
                    else
                    {
                        // fallback
                        value = field.GetValue(mb) as Object;
                    }

                    if (value != null) injectWiredCount++;

                    string owner = mb.GetType().Name;
                    string targetName = value != null ? value.GetType().Name : "None";
                    string line = $"{owner}.{field.Name} → {targetName}";
                    localLines.Add((value != null, line));
                }
                else if (System.Attribute.IsDefined(field, typeof(SceneInjectAttribute)))
                {
                    sceneInjectFieldCount++;

                    bool inSceneRegistry = IsTypeRegisteredInScene(field.FieldType);
                    var attr = field.GetCustomAttribute<SceneInjectAttribute>();
                    bool optional = attr != null && attr.Optional;

                    string owner = mb.GetType().Name;
                    string line = $"{owner}.{field.Name} : {field.FieldType.Name}";
                    sceneLines.Add((inSceneRegistry, optional, line));
                }
                else if (System.Attribute.IsDefined(field, typeof(GlobalInjectAttribute)))
                {
                    globalInjectFieldCount++;

                    bool inRegistry = IsTypeRegisteredInMaster(field.FieldType);
                    var attr = field.GetCustomAttribute<GlobalInjectAttribute>();
                    bool optional = attr != null && attr.Optional;

                    string owner = mb.GetType().Name;
                    string line = $"{owner}.{field.Name} : {field.FieldType.Name}";
                    globalLines.Add((inRegistry, optional, line));
                }
            }
        }

        // ----- Local [Inject] -----
        var boxStyle = new GUIStyle("HelpBox")
        {
            richText = true,
            padding = new RectOffset(8, 8, 6, 6)
        };

        EditorGUILayout.BeginVertical(boxStyle);
        EditorGUILayout.LabelField("Local Dependencies [Inject]", EditorStyles.boldLabel);

        if (injectFieldCount == 0)
        {
            EditorGUILayout.LabelField(
                "<i>There are no components with the [Inject] field in this root layer.</i>",
                new GUIStyle(EditorStyles.label) { richText = true });
        }
        else
        {
            EditorGUILayout.LabelField(
                $"<color=#7BD88F><b>{injectWiredCount}</b></color> / {injectFieldCount} fields linked",
                new GUIStyle(EditorStyles.label) { richText = true });

            foreach (var (linked, text) in localLines)
            {
                string bulletColor = linked ? "#7BD88F" : "#FFB374";
                string status = linked ? "<color=#7BD88F>[OK]</color>" : "<color=#FF6B6B>[Missing]</color>";
                EditorGUILayout.LabelField(
                    $"<color={bulletColor}>●</color> {text}  {status}",
                    new GUIStyle(EditorStyles.label) { richText = true });
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // ----- Scene [SceneInject] -----
        var boxStyleScene = new GUIStyle("HelpBox")
        {
            richText = true,
            padding = new RectOffset(8, 8, 6, 6)
        };

        EditorGUILayout.BeginVertical(boxStyleScene);
        EditorGUILayout.LabelField("Scene Dependencies [SceneInject]", EditorStyles.boldLabel);

        if (sceneInjectFieldCount == 0)
        {
            EditorGUILayout.LabelField(
                "<i>There are no components with the [SceneInject] field in this root layer.</i>",
                new GUIStyle(EditorStyles.label) { richText = true });
        }
        else
        {
            EditorGUILayout.LabelField(
                $"{sceneInjectFieldCount} fields require SceneInstaller managers",
                new GUIStyle(EditorStyles.label) { richText = true });

            foreach (var (inSceneRegistry, optional, text) in sceneLines)
            {
                string bulletColor;
                string status;

                if (inSceneRegistry)
                {
                    bulletColor = "#a6e3ff";
                    status = "<color=#7BD88F>[Registered]</color>";
                }
                else if (optional)
                {
                    bulletColor = "#888888";
                    status = "<color=#888888>[Optional — not registered]</color>";
                }
                else
                {
                    bulletColor = "#FFB374";
                    status = "<color=#FF6B6B>[Not in scene registry]</color>";
                }

                EditorGUILayout.LabelField(
                    $"<color={bulletColor}>●</color> {text}  {status}",
                    new GUIStyle(EditorStyles.label) { richText = true });
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // ----- Global [GlobalInject] -----
        var boxStyleGlobal = new GUIStyle("HelpBox")
        {
            richText = true,
            padding = new RectOffset(8, 8, 6, 6)
        };

        EditorGUILayout.BeginVertical(boxStyleGlobal);
        EditorGUILayout.LabelField("Global Dependencies [GlobalInject]", EditorStyles.boldLabel);

        if (globalInjectFieldCount == 0)
        {
            EditorGUILayout.LabelField(
                "<i>There are no components with the [GlobalInject] field in this root layer.</i>",
                new GUIStyle(EditorStyles.label) { richText = true });
        }
        else
        {
            EditorGUILayout.LabelField(
                $"{globalInjectFieldCount} fields require Manager Layer from MasterInstaller",
                new GUIStyle(EditorStyles.label) { richText = true });

            foreach (var (inRegistry, optional, text) in globalLines)
            {
                string bulletColor;
                string status;

                if (inRegistry)
                {
                    bulletColor = "#7BB4FF";
                    status = "<color=#7BD88F>[Registered]</color>";
                }
                else if (optional)
                {
                    bulletColor = "#888888";
                    status = "<color=#888888>[Optional — not registered]</color>";
                }
                else
                {
                    bulletColor = "#FFB374";
                    status = "<color=#FF6B6B>[Not in registry]</color>";
                }

                EditorGUILayout.LabelField(
                    $"<color={bulletColor}>●</color> {text}  {status}",
                    new GUIStyle(EditorStyles.label) { richText = true });
            }
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Master Installer 런 타임 검증 
    /// </summary>
    private bool IsTypeRegisteredInMaster(System.Type fieldType)
    {
        if (fieldType == null) return false;

        var master = Object.FindObjectOfType<MasterInstaller>();
        if (master == null) return false;

        var so = new SerializedObject(master);
        var prop = so.FindProperty("_globalReferrals");
        if (prop == null) return false;

        for (int i = 0; i < prop.arraySize; i++)
        {
            var element = prop.GetArrayElementAtIndex(i);
            var comp = element.objectReferenceValue as Component;
            if (comp == null) continue;

            if (fieldType.IsAssignableFrom(comp.GetType()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Scene Installer 런 타임 검증 
    /// </summary>
    private bool IsTypeRegisteredInScene(System.Type fieldType)
    {
        if (fieldType == null) return false;

        var sceneInstaller = Object.FindObjectOfType<SceneInstaller>();
        if (sceneInstaller == null) return false;

        var so = new SerializedObject(sceneInstaller);
        var prop = so.FindProperty("_sceneReferrals");
        if (prop == null) return false;

        for (int i = 0; i < prop.arraySize; i++)
        {
            var element = prop.GetArrayElementAtIndex(i);
            var comp = element.objectReferenceValue as Component;
            if (comp == null) continue;

            if (fieldType.IsAssignableFrom(comp.GetType()))
                return true;
        }

        return false;
    }
}

[CustomEditor(typeof(MasterInstaller))]
public class MasterInstallerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MasterInstaller installer = (MasterInstaller)target;

        // ---------------- Refresh ----------------
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("🔄 Refresh Global Registry", GUILayout.Height(20)))
        {
            installer.RefreshRegistry();
        }
        GUI.backgroundColor = Color.white;

        // ---------------- 레지스트리 시각화 ----------------
        DrawRegistryOverview();

        EditorGUILayout.Space(8);

        // m_Script 필드는 유니티에서 Mono 스크립트 표기하는 ReadOnly 필드. 딱히 필요 없으니 숨김.
        // 따로 빼려면 주석.
        serializedObject.Update();
        var prop = serializedObject.GetIterator();
        bool enterChildren = true;

        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (prop.name == "m_Script")
                continue;

            // _globalReferrals 는 읽기 전용으로 표시
            bool isRegistry = prop.name == "_globalReferrals";
            if (isRegistry) GUI.enabled = false;

            EditorGUILayout.PropertyField(prop, true);

            if (isRegistry) GUI.enabled = true;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawRegistryOverview()
    {
        var registryProp = serializedObject.FindProperty("_globalReferrals");
        int count = registryProp != null ? registryProp.arraySize : 0;

        var boxStyle = new GUIStyle("HelpBox")
        {
            richText = true,
            padding = new RectOffset(8, 8, 6, 6)
        };

        EditorGUILayout.BeginVertical(boxStyle);

        string headerColor = count > 0 ? "#7BB4FF" : "#FFB374";
        EditorGUILayout.LabelField(
            $"<color={headerColor}><b>Global Registry</b></color>  ({count} managers)",
            new GUIStyle(EditorStyles.label) { richText = true });

        if (count == 0)
        {
            EditorGUILayout.LabelField(
                "<i>There are no registered Global Managers. Press the 'Refresh Global Registry' button to scan the scene's [Referral] component.</i>",
                new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true });
        }
        else
        {
            int displayLimit = 8;
            for (int i = 0; i < count && i < displayLimit; i++)
            {
                var element = registryProp.GetArrayElementAtIndex(i);
                var obj = element.objectReferenceValue as Component;
                if (obj == null) continue;

                string line = $"• <b>{obj.GetType().Name}</b>  <color=#888888>({obj.name})</color>";
                EditorGUILayout.LabelField(line, new GUIStyle(EditorStyles.label) { richText = true });
            }

            if (count > displayLimit)
            {
                EditorGUILayout.LabelField(
                    $"… and <b>{count - displayLimit}</b> more",
                    new GUIStyle(EditorStyles.miniLabel) { richText = true });
            }
        }

        EditorGUILayout.EndVertical();
    }
}

[CustomEditor(typeof(SceneInstaller))]
public class SceneInstallerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SceneInstaller installer = (SceneInstaller)target;

        // ---------------- Refresh ----------------
        GUI.backgroundColor = new Color(0.6f, 1f, 0.9f);
        if (GUILayout.Button("🔄 Refresh Scene Registry", GUILayout.Height(20)))
        {
            installer.RefreshSceneRegistry();
        }
        GUI.backgroundColor = Color.white;

        // ---------------- 레지스트리 시각화 ----------------
        DrawSceneRegistryOverview();

        EditorGUILayout.Space(8);

        // m_Script를 숨기고 나머지 필드만 표시
        serializedObject.Update();
        var prop = serializedObject.GetIterator();
        bool enterChildren = true;

        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (prop.name == "m_Script")
                continue;

            // _sceneReferrals 는 읽기 전용으로 표시
            bool isRegistry = prop.name == "_sceneReferrals";
            if (isRegistry) GUI.enabled = false;

            EditorGUILayout.PropertyField(prop, true);

            if (isRegistry) GUI.enabled = true;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSceneRegistryOverview()
    {
        var registryProp = serializedObject.FindProperty("_sceneReferrals");
        int count = registryProp != null ? registryProp.arraySize : 0;

        var boxStyle = new GUIStyle("HelpBox")
        {
            richText = true,
            padding = new RectOffset(8, 8, 6, 6)
        };

        EditorGUILayout.BeginVertical(boxStyle);

        string headerColor = count > 0 ? "#a6e3ff" : "#FFB374";
        EditorGUILayout.LabelField(
            $"<color={headerColor}><b>Scene Registry</b></color>  ({count} managers)",
            new GUIStyle(EditorStyles.label) { richText = true });

        if (count == 0)
        {
            EditorGUILayout.LabelField(
                "<i>There are no registered Scene Managers. Press the 'Refresh Scene Registry' button to scan the scene's [SceneReferral] component.</i>",
                new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true });
        }
        else
        {
            int displayLimit = 8;
            for (int i = 0; i < count && i < displayLimit; i++)
            {
                var element = registryProp.GetArrayElementAtIndex(i);
                var obj = element.objectReferenceValue as Component;
                if (obj == null) continue;

                string line = $"• <b>{obj.GetType().Name}</b>  <color=#888888>({obj.name})</color>";
                EditorGUILayout.LabelField(line, new GUIStyle(EditorStyles.label) { richText = true });
            }

            if (count > displayLimit)
            {
                EditorGUILayout.LabelField(
                    $"… and <b>{count - displayLimit}</b> more",
                    new GUIStyle(EditorStyles.miniLabel) { richText = true });
            }
        }

        EditorGUILayout.EndVertical();
    }
}
#endif