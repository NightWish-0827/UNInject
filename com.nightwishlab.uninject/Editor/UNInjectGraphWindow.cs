#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UNInject 의존성 그래프를 시각화하는 에디터 창.
///
/// 표시 요소:
///   - Installer 노드 (MasterInstaller / SceneInstaller)
///   - [Referral] / [SceneReferral] 노드 (씬에 존재하는 Manager 컴포넌트)
///   - [Inject Target] 노드 (GlobalInject / SceneInject 필드를 가진 타입)
///   - 등록 엣지 (Installer → Referral)
///   - 주입 엣지 (Referral → Inject Target, Roslyn 플랜 있으면 초록, 폴백이면 노랑)
///
/// 사용법: Window > UNInject > Dependency Graph
/// </summary>
public class UNInjectGraphWindow : EditorWindow
{
    private UNInjectGraphView _graphView;

    [MenuItem("Window/UNInject/Dependency Graph")]
    public static void ShowWindow()
    {
        var window = GetWindow<UNInjectGraphWindow>("UNInject: D.I Graph");
        window.minSize = new Vector2(900, 600);
        window.Show();
    }

    private void OnEnable()
    {
        BuildUI();
    }

    private void OnDisable()
    {
        if (_graphView != null)
            rootVisualElement.Remove(_graphView);
    }

    private void BuildUI()
    {
        rootVisualElement.Clear();

        // ── Toolbar ──────────────────────────────────────────────────────────
        var toolbar = new UnityEditor.UIElements.Toolbar();

        var refreshBtn = new UnityEditor.UIElements.ToolbarButton(RefreshGraph)
        {
            text = "Refresh",
            tooltip = "현재 씬 상태를 기반으로 그래프를 다시 그립니다."
        };
        toolbar.Add(refreshBtn);

        var legendLabel = new Label("  ● Global  ● Scene  ● Inject Target  ● Missing")
        {
            style = { color = Color.gray, unityFontStyleAndWeight = FontStyle.Italic }
        };
        toolbar.Add(legendLabel);

        rootVisualElement.Add(toolbar);

        // ── GraphView ─────────────────────────────────────────────────────────
        _graphView = new UNInjectGraphView();
        _graphView.StretchToParentSize();
        rootVisualElement.Add(_graphView);

        RefreshGraph();
    }

    private void RefreshGraph()
    {
        _graphView?.Populate();
    }
}

/// <summary>
/// GraphView 구현체. 씬 스캔 결과를 바탕으로 노드/엣지를 생성하고 배치한다.
/// </summary>
internal class UNInjectGraphView : GraphView
{
    private static readonly Color ColGlobal   = new Color(0.3f, 0.8f, 0.9f);
    private static readonly Color ColScene    = new Color(0.4f, 0.9f, 0.5f);
    private static readonly Color ColInject   = new Color(0.95f, 0.85f, 0.3f);
    private static readonly Color ColMissing  = new Color(0.9f, 0.3f, 0.3f);
    private static readonly Color ColInstaller = new Color(0.7f, 0.6f, 1.0f);

    internal UNInjectGraphView()
    {
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        style.flexGrow = 1;
        style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f));

        var grid = new GridBackground();
        grid.StretchToParentSize();
        Insert(0, grid);
    }

    internal void Populate()
    {
        DeleteElements(graphElements);

        // v2.0: RegistryKey(Type, Id) 복합 키로 교체하여 동일 타입의 Named 바인딩 덮어씌움 방지
        var referralPortMap = new Dictionary<RegistryKey, Port>();
        float xBase   = 50f;
        float yGlobal = 80f;
        float yScene  = 420f;

        // ── Installer ノード ─────────────────────────────────────────────────
        var masterNode = MakeInstallerNode("MasterInstaller\n[Global]", ColInstaller,
                                           new Vector2(xBase, yGlobal));
        AddElement(masterNode);
        var masterOut = masterNode.outputContainer.Q<Port>();

        var sceneNode = MakeInstallerNode("SceneInstaller\n[Scene]", ColInstaller,
                                          new Vector2(xBase, yScene));
        AddElement(sceneNode);
        var sceneOut = sceneNode.outputContainer.Q<Port>();

        // ── Referral 스캔 ────────────────────────────────────────────────────
        var allComponents = Resources.FindObjectsOfTypeAll<Component>();
        float xRef      = 320f;
        int   idxGlobal = 0;
        int   idxScene  = 0;

        foreach (var comp in allComponents)
        {
            if (comp == null || !comp.gameObject.scene.IsValid()) continue;

            var compType   = comp.GetType();
            var attrGlobal = compType.GetCustomAttribute<ReferralAttribute>();
            var attrScene  = compType.GetCustomAttribute<SceneReferralAttribute>();
            bool isGlobal  = attrGlobal != null;
            bool isScene   = attrScene  != null;

            if (!isGlobal && !isScene) continue;

            // v2.0: Named 바인딩 Id 추출 — 노드 라벨과 portMap 키에 모두 반영
            string refId     = attrGlobal?.Id ?? attrScene?.Id ?? string.Empty;
            string nodeLabel = string.IsNullOrEmpty(refId)
                ? compType.Name
                : $"{compType.Name} [id:\"{refId}\"]";

            float yPos = isGlobal
                ? yGlobal + idxGlobal++ * 90f
                : yScene  + idxScene++  * 90f;

            var refNode = MakeReferralNode(nodeLabel, comp.gameObject.name,
                isGlobal ? ColGlobal : ColScene, new Vector2(xRef, yPos));
            AddElement(refNode);

            var refIn  = refNode.inputContainer.Q<Port>();
            var refOut = refNode.outputContainer.Q<Port>();

            var installerOut = isGlobal ? masterOut : sceneOut;
            if (installerOut != null && refIn != null)
                AddElement(installerOut.ConnectTo(refIn));

            referralPortMap[new RegistryKey(compType, refId)] = refOut;
        }

        // ── Inject Target スキャン ────────────────────────────────────────────
        const BindingFlags Flags = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;

        float xInject = 660f;
        float yInject = 60f;
        var processed = new HashSet<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkipAssembly(assembly.GetName().Name)) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (processed.Contains(type)) continue;

                // v2.0: (field, isGlobal, hasPlan, id) 로 확장 — Named 바인딩 Id 포함
                var injectFields = new List<(FieldInfo field, bool isGlobal, bool hasPlan, string id)>();

                var current = type;
                while (current != null && current != typeof(object))
                {
                    foreach (var field in current.GetFields(Flags))
                    {
                        var gAttr = field.GetCustomAttribute<GlobalInjectAttribute>();
                        var sAttr = field.GetCustomAttribute<SceneInjectAttribute>();
                        bool g = gAttr != null;
                        bool s = sAttr != null;
                        if (!g && !s) continue;

                        // v2.0: 필드에 선언된 Named 바인딩 Id 추출
                        string fieldId = gAttr?.Id ?? sAttr?.Id ?? string.Empty;
                        bool plan = g ? TypeDataCache.HasGeneratedGlobalPlan(type)
                                      : TypeDataCache.HasGeneratedScenePlan(type);
                        injectFields.Add((field, g, plan, fieldId));
                    }
                    current = current.BaseType;
                }

                if (injectFields.Count == 0) continue;
                processed.Add(type);

                bool allConnected = true;
                foreach (var (field, _, _, fid) in injectFields)
                {
                    if (!referralPortMap.ContainsKey(new RegistryKey(field.FieldType, fid)))
                    {
                        allConnected = false;
                        break;
                    }
                }

                var nodeColor = allConnected ? ColInject : ColMissing;
                var injectNode = MakeInjectNode(type.Name, nodeColor, new Vector2(xInject, yInject));
                AddElement(injectNode);
                yInject += 110f;

                var injectIn = injectNode.inputContainer.Q<Port>();

                foreach (var (field, _, hasPlan, fid) in injectFields)
                {
                    if (!referralPortMap.TryGetValue(new RegistryKey(field.FieldType, fid), out var fromPort)) continue;
                    if (injectIn == null) continue;

                    var edge = fromPort.ConnectTo(injectIn);
                    edge.edgeControl.inputColor  = hasPlan ? Color.green : Color.yellow;
                    edge.edgeControl.outputColor = hasPlan ? Color.green : Color.yellow;
                    AddElement(edge);
                }
            }
        }

        FrameAll();
    }

    // ── ノードファクトリ ──────────────────────────────────────────────────────

    private static UNGraphNode MakeInstallerNode(string title, Color col, Vector2 pos)
    {
        var node = new UNGraphNode(title, col);
        node.SetPosition(new Rect(pos, new Vector2(170, 52)));
        var outPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output,
                                        Port.Capacity.Multi, typeof(Component));
        outPort.portName = "Scope";
        node.outputContainer.Add(outPort);
        node.RefreshPorts();
        node.RefreshExpandedState();
        return node;
    }

    private static UNGraphNode MakeReferralNode(string typeName, string goName, Color col, Vector2 pos)
    {
        var node = new UNGraphNode($"{typeName}\n<{goName}>", col);
        node.SetPosition(new Rect(pos, new Vector2(210, 62)));

        var inPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input,
                                       Port.Capacity.Single, typeof(Component));
        inPort.portName = "";
        node.inputContainer.Add(inPort);

        var outPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output,
                                         Port.Capacity.Multi, typeof(Component));
        outPort.portName = "";
        node.outputContainer.Add(outPort);

        node.RefreshPorts();
        node.RefreshExpandedState();
        return node;
    }

    private static UNGraphNode MakeInjectNode(string typeName, Color col, Vector2 pos)
    {
        var node = new UNGraphNode($"[Inject] {typeName}", col);
        node.SetPosition(new Rect(pos, new Vector2(220, 52)));

        var inPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input,
                                       Port.Capacity.Multi, typeof(Component));
        inPort.portName = "deps";
        node.inputContainer.Add(inPort);

        node.RefreshPorts();
        node.RefreshExpandedState();
        return node;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        var result = new List<Port>();
        ports.ForEach(p =>
        {
            if (p != startPort && p.node != startPort.node && p.direction != startPort.direction)
                result.Add(p);
        });
        return result;
    }

    private static bool ShouldSkipAssembly(string name)
        => UNInjectEditorUtility.ShouldSkipAssembly(name);
}

/// <summary>색상 헤더를 가진 범용 그래프 노드.</summary>
internal class UNGraphNode : Node
{
    internal UNGraphNode(string title, Color headerColor)
    {
        this.title = title;
        titleContainer.style.backgroundColor = new StyleColor(headerColor * 0.6f);
        titleContainer.style.paddingTop    = 4;
        titleContainer.style.paddingBottom = 4;
    }
}
#endif
