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

        // 缓存预览数据，方便重绘
        private BuildContext _previewContext;

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
            // 【Undo】1. 监听全局撤销重做事件
            Undo.undoRedoPerformed += OnUndoRedo;

            ConstructGraphView();
            GenerateToolbar();
            CreatePreviewPanel();

            if (_currentAsset != null) LoadGraph();
        }

        private void OnDisable()
        {
            // 【Undo】2. 移除监听
            Undo.undoRedoPerformed -= OnUndoRedo;

            if (_currentAsset != null)
            {
                // 关闭窗口时执行一次完整的磁盘保存
                SaveGraph("Close Window");
                AssetDatabase.SaveAssets();
            }
        }

        // 【Undo】3. 当用户按 Ctrl+Z 时触发
        private void OnUndoRedo()
        {
            if (_currentAsset != null)
            {
                // 重新从 Asset 加载 View，因为 Asset 的数据已经被 Unity 回滚了
                LoadGraph();
            }
        }

        private void ConstructGraphView()
        {
            if (_graphView != null) rootVisualElement.Remove(_graphView);

            _graphView = new BuildGraphView
            {
                name = "Build Graph"
            };

            // 【Undo】4. 监听 GraphView 的拓扑变化 (移动/删除/连线)
            // 注意：这需要 BuildGraphView.cs 中暴露了 OnGraphViewChanged 委托
            _graphView.OnGraphViewChanged = HandleGraphViewChanges;

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

        // 【Undo】5. 处理 GraphView 的变化
        private GraphViewChange HandleGraphViewChanges(GraphViewChange change)
        {
            string undoName = "Graph Change";
            if (change.movedElements != null) undoName = "Move Nodes";
            if (change.elementsToRemove != null) undoName = "Delete Elements";
            if (change.edgesToCreate != null) undoName = "Connect Edge";

            // 延迟一帧保存，等待 View 更新完毕
            rootVisualElement.schedule.Execute(() =>
            {
                SaveGraph(undoName);
            }).ExecuteLater(10);

            return change;
        }

        private void CreateNode<T>(Vector2 mousePos) where T : BaseBuildNode, new()
        {
            // 【Undo】创建前记录状态
            SaveGraph("Before Create Node");

            var node = new T();
            node.Initialize();

            // 【Undo】6. 绑定节点内部数据的变更 (TextField 修改等)
            // 当节点内部调用 NotifyChange 时，触发保存
            node.OnDataChanged = () => SaveGraph("Node Data Change");

            Vector2 graphPos = _graphView.contentViewContainer.WorldToLocal(mousePos);
            node.SetPosition(new Rect(graphPos, Vector2.zero));
            _graphView.AddElement(node);

            // 【Undo】创建后记录状态
            SaveGraph("Create Node");
        }

        // --- Save Logic (改造支持 Undo) ---
        private void SaveGraph(string undoName = "Save Graph")
        {
            if (_currentAsset == null) return;

            // 【Undo】7. 关键：记录快照
            Undo.RecordObject(_currentAsset, undoName);

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

            // 标记脏数据，但不强制写磁盘 (为了性能)
            EditorUtility.SetDirty(_currentAsset);
        }

        // --- Load Logic (改造支持事件绑定) ---
        private void LoadGraph()
        {
            if (_currentAsset == null) return;

            // 清空现有元素
            _graphView.DeleteElements(_graphView.graphElements);

            var nodeDict = new Dictionary<string, BaseBuildNode>();

            // 恢复节点
            foreach (var nodeData in _currentAsset.Nodes)
            {
                var nodeType = System.Type.GetType(nodeData.NodeType);
                if (nodeType == null) nodeType = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);
                if (nodeType == null) continue;

                var node = System.Activator.CreateInstance(nodeType) as BaseBuildNode;
                node.Initialize();
                node.GUID = nodeData.NodeGUID;
                node.title = nodeData.Title;
                node.SetPosition(new Rect(nodeData.Position, Vector2.zero));
                node.LoadFromJSON(nodeData.JsonData);

                // 【Undo】8. 恢复节点时，重新绑定数据变更事件
                node.OnDataChanged = () => SaveGraph("Node Value Change");

                _graphView.AddElement(node);
                nodeDict.Add(node.GUID, node);
            }

            // 恢复连线
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

        // --- Toolbar ---
        private void GenerateToolbar()
        {
            var existingToolbar = rootVisualElement.Q<UnityEditor.UIElements.Toolbar>();
            if (existingToolbar != null) rootVisualElement.Remove(existingToolbar);

            var toolbar = new UnityEditor.UIElements.Toolbar();

            // 【Undo】Save 按钮改为强制写磁盘
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() =>
            {
                SaveGraph("Force Save");
                AssetDatabase.SaveAssets();
            })
            { text = "Force Save" });

            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => LoadGraph()) { text = "Reload" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => PreviewSelectedNode()) { text = "Preview Selection" });
            toolbar.Add(new Label(_currentAsset != null ? $"File: {_currentAsset.name}" : "No Asset") { style = { marginLeft = 10, unityTextAlign = TextAnchor.MiddleLeft } });

            rootVisualElement.Add(toolbar);
        }

        // --- 预览面板相关 (保持 UI Toolkit 写法) ---
        private void CreatePreviewPanel()
        {
            _previewContainer = new VisualElement();
            _previewContainer.name = "PreviewPanel";
            _previewContainer.style.position = Position.Absolute;
            _previewContainer.style.bottom = 0;
            _previewContainer.style.left = 0;
            _previewContainer.style.right = 0;
            _previewContainer.style.height = 200;
            _previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            _previewContainer.style.borderTopWidth = 2;
            _previewContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            _previewContainer.visible = false;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.height = 25;
            header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            header.style.paddingLeft = 10;
            header.style.alignItems = Align.Center;

            var titleLabel = new Label("Preview Results");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1;

            var closeBtn = new Button(() => { _previewContainer.visible = false; }) { text = "X" };
            closeBtn.style.width = 25;

            header.Add(titleLabel);
            header.Add(closeBtn);

            _previewScrollView = new ScrollView();
            _previewScrollView.style.paddingLeft = 10;
            _previewScrollView.style.paddingTop = 10;

            _previewContainer.Add(header);
            _previewContainer.Add(_previewScrollView);
            rootVisualElement.Add(_previewContainer);
        }

        private void UpdatePreviewPanel(BuildContext context)
        {
            _previewScrollView.Clear();

            if (context == null)
            {
                _previewContainer.visible = false;
                return;
            }

            _previewContainer.visible = true;

            if (context.Logs.Length > 0)
            {
                var logHeader = new Label("Execution Logs:");
                logHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                logHeader.style.color = new Color(0.7f, 0.7f, 0.7f);
                _previewScrollView.Add(logHeader);

                var logContent = new Label(context.Logs.ToString());
                logContent.style.whiteSpace = WhiteSpace.Normal;
                logContent.style.marginBottom = 15;
                _previewScrollView.Add(logContent);
            }

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
                    if (!string.IsNullOrEmpty(asset.BundleName)) row.style.color = new Color(0.8f, 0.8f, 1f);
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
                UpdatePreviewPanel(context);
            }
            else
            {
                Debug.LogWarning("Please select a node first.");
                UpdatePreviewPanel(null);
            }
        }
    }
}