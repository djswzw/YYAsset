using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using YY.Build.Data;
using YY.Build.Core;
using YY.Build.Graph.Nodes;
using System.Linq;
using System.Collections.Generic;

namespace YY.Build.Graph
{
    public class BuildGraphWindow : EditorWindow
    {
        private BuildGraphView _graphView;
        private BuildGraphAsset _currentAsset;

        private VisualElement _previewContainer;
        private ScrollView _previewScrollView;

        [UnityEditor.Callbacks.OnOpenAsset(1)]
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            var asset = EditorUtility.EntityIdToObject(entityId) as BuildGraphAsset;
            if (asset != null)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        public static void Open(BuildGraphAsset asset)
        {
            var window = GetWindow<BuildGraphWindow>("Build Pipeline Graph");
            window._currentAsset = asset;
            window.LoadGraph();
        }

        private void OnEnable()
        {
            ConstructGraphView();
            GenerateToolbar();
            CreatePreviewPanel(); // 【新增】初始化预览面板结构

            if (_currentAsset != null) LoadGraph();
        }

        private void OnDisable()
        {
            if (_currentAsset != null) SaveGraph();
        }

        private void ConstructGraphView()
        {
            if (_graphView != null) rootVisualElement.Remove(_graphView);

            _graphView = new BuildGraphView
            {
                name = "Build Graph"
            };
            _graphView.StretchToParentSize();
            // 确保 GraphView 在最底层
            rootVisualElement.Insert(0, _graphView);

            var menu = new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Source/Directory Node", action => CreateNode<DirectoryNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Process/Filter Node", action => CreateNode<FilterNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Strategy/Grouper Node", action => CreateNode<GrouperNode>(action.eventInfo.localMousePosition));
            });
            _graphView.AddManipulator(menu);
        }

        // --- 【关键】创建 UI Toolkit 风格的预览面板 ---
        private void CreatePreviewPanel()
        {
            // 1. 创建容器
            _previewContainer = new VisualElement();
            _previewContainer.name = "PreviewPanel";

            // 2. 设置绝对定位样式 (固定在底部)
            _previewContainer.style.position = Position.Absolute;
            _previewContainer.style.bottom = 0;
            _previewContainer.style.left = 0;
            _previewContainer.style.right = 0;
            _previewContainer.style.height = 200;
            _previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f); // 深色背景
            _previewContainer.style.borderTopWidth = 2;
            _previewContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            _previewContainer.visible = false; // 默认隐藏

            // 3. 标题栏
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.height = 25;
            header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            header.style.paddingLeft = 10;
            header.style.alignItems = Align.Center;

            var titleLabel = new Label("Preview Results");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1;

            var closeBtn = new Button(() =>
            {
                _previewContainer.visible = false;
            })
            { text = "X" };
            closeBtn.style.width = 25;

            header.Add(titleLabel);
            header.Add(closeBtn);

            // 4. 内容滚动区
            _previewScrollView = new ScrollView();
            _previewScrollView.style.paddingLeft = 10;
            _previewScrollView.style.paddingTop = 10;

            _previewContainer.Add(header);
            _previewContainer.Add(_previewScrollView);

            // 5. 添加到根节点 (保证在 GraphView 之上)
            rootVisualElement.Add(_previewContainer);
        }

        // --- 【关键】刷新预览数据 ---
        private void UpdatePreviewPanel(BuildContext context)
        {
            _previewScrollView.Clear();

            if (context == null)
            {
                _previewContainer.visible = false;
                return;
            }

            _previewContainer.visible = true;

            // 1. 显示日志
            if (context.Logs.Length > 0)
            {
                var logHeader = new Label("Execution Logs:");
                logHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                logHeader.style.color = new Color(0.7f, 0.7f, 0.7f);
                _previewScrollView.Add(logHeader);

                var logContent = new Label(context.Logs.ToString());
                logContent.style.whiteSpace = WhiteSpace.Normal; // 自动换行
                logContent.style.marginBottom = 15;
                _previewScrollView.Add(logContent);
            }

            // 2. 显示资产
            var assetHeader = new Label($"Assets Output ({context.Assets.Count}):");
            assetHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            assetHeader.style.color = new Color(0.4f, 0.8f, 0.4f);
            _previewScrollView.Add(assetHeader);

            if (context.Assets.Count == 0)
            {
                _previewScrollView.Add(new Label("  (Empty List)") { style = { color = Color.gray } });
            }
            else
            {
                foreach (var asset in context.Assets)
                {
                    string bundleText = string.IsNullOrEmpty(asset.BundleName) ? "[No Bundle]" : $"[{asset.BundleName}]";
                    var row = new Label($"{bundleText}  {asset.AssetPath}");

                    // 给 Bundle 名加个颜色区分
                    if (!string.IsNullOrEmpty(asset.BundleName))
                    {
                        row.style.color = new Color(0.8f, 0.8f, 1f); // 淡蓝色
                    }
                    _previewScrollView.Add(row);
                }
            }
        }

        private void PreviewSelectedNode()
        {
            if (_graphView.selection.Count > 0 && _graphView.selection[0] is BaseBuildNode selectedNode)
            {
                Debug.Log($"[BuildGraph] Previewing: {selectedNode.title}");
                var context = GraphRunner.Run(selectedNode);

                // 【调用 UI 更新】
                UpdatePreviewPanel(context);
            }
            else
            {
                Debug.LogWarning("Please select a node first.");
                UpdatePreviewPanel(null);
            }
        }

        private void GenerateToolbar()
        {
            var existingToolbar = rootVisualElement.Q<UnityEditor.UIElements.Toolbar>();
            if (existingToolbar != null) rootVisualElement.Remove(existingToolbar);

            var toolbar = new UnityEditor.UIElements.Toolbar();
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => SaveGraph()) { text = "Save Asset" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => LoadGraph()) { text = "Reload" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => PreviewSelectedNode()) { text = "Preview Selection" });
            toolbar.Add(new Label(_currentAsset != null ? $"File: {_currentAsset.name}" : "No Asset") { style = { marginLeft = 10, unityTextAlign = TextAnchor.MiddleLeft } });

            rootVisualElement.Add(toolbar);
        }

        private void CreateNode<T>(Vector2 mousePos) where T : BaseBuildNode, new()
        {
            var node = new T();
            node.Initialize();
            Vector2 graphPos = _graphView.contentViewContainer.WorldToLocal(mousePos);
            node.SetPosition(new Rect(graphPos, Vector2.zero));
            _graphView.AddElement(node);
        }

        private void SaveGraph()
        {
            if (_currentAsset == null) return;
            _currentAsset.Nodes.Clear();
            _currentAsset.Edges.Clear();

            foreach (var node in _graphView.nodes.ToList().Cast<BaseBuildNode>())
            {
                _currentAsset.Nodes.Add(new BuildNodeData
                {
                    NodeGUID = node.GUID,
                    Position = node.GetPosition().position,
                    NodeType = node.GetType().FullName,
                    Title = node.title,
                    JsonData = node.SaveToJSON()
                });
            }
            foreach (var edge in _graphView.edges.ToList())
            {
                var outputNode = edge.output.node as BaseBuildNode;
                var inputNode = edge.input.node as BaseBuildNode;
                _currentAsset.Edges.Add(new BuildEdgeData
                {
                    BaseNodeGUID = outputNode.GUID,
                    BasePortName = edge.output.portName,
                    TargetNodeGUID = inputNode.GUID,
                    TargetPortName = edge.input.portName
                });
            }
            EditorUtility.SetDirty(_currentAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildGraph] Saved.");
        }

        private void LoadGraph()
        {
            if (_currentAsset == null) return;
            _graphView.DeleteElements(_graphView.graphElements);
            var nodeDict = new Dictionary<string, BaseBuildNode>();

            foreach (var nodeData in _currentAsset.Nodes)
            {
                var nodeType = System.Type.GetType(nodeData.NodeType);
                if (nodeType == null) nodeType = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);
                if (nodeType == null) continue;

                var node = System.Activator.CreateInstance(nodeType) as BaseBuildNode;
                node.Initialize();
                node.GUID = nodeData.NodeGUID; node.title = nodeData.Title;
                node.SetPosition(new Rect(nodeData.Position, Vector2.zero));
                node.LoadFromJSON(nodeData.JsonData);
                _graphView.AddElement(node);
                nodeDict.Add(node.GUID, node);
            }

            foreach (var edgeData in _currentAsset.Edges)
            {
                if (nodeDict.TryGetValue(edgeData.BaseNodeGUID, out var outNode) && nodeDict.TryGetValue(edgeData.TargetNodeGUID, out var inNode))
                {
                    var outPort = outNode.outputContainer.Q<Port>(edgeData.BasePortName);
                    var inPort = inNode.inputContainer.Q<Port>(edgeData.TargetPortName);
                    if (outPort != null && inPort != null) _graphView.AddElement(outPort.ConnectTo(inPort));
                }
            }
        }
    }
}