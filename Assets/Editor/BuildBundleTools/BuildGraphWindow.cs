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
                evt.menu.AppendAction("Process/Dependency Node", action => CreateNode<DependencyNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Process/Merge Node", action => CreateNode<MergeNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Process/Portal Sender", action => CreateNode<PortalSenderNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Process/Portal Receiver", action => CreateNode<PortalReceiverNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Strategy/Grouper Node", action => CreateNode<GrouperNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Strategy/Deduplicator Node", action => CreateNode<DeduplicatorNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Export/Build Bundle Node", action => CreateNode<BuildBundleNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Export/Apply to Editor", action => CreateNode<ApplyToEditorNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Debug/Asset List Viewer", action => CreateNode<ViewerNode>(action.eventInfo.localMousePosition));
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
                try
                {
                    var nodeType = System.Type.GetType(nodeData.NodeType);
                    if (nodeType == null) nodeType = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);
                    if (nodeType == null) { Debug.LogError($"Missing Node Type: {nodeData.NodeType}"); continue; }

                    var node = System.Activator.CreateInstance(nodeType) as BaseBuildNode;
                    node.Initialize();
                    node.GUID = nodeData.NodeGUID;
                    node.title = nodeData.Title;
                    node.SetPosition(new Rect(nodeData.Position, Vector2.zero));

                    // 恢复数据
                    node.LoadFromJSON(nodeData.JsonData);

                    // 绑定事件
                    node.OnDataChanged = () => SaveGraph("Node Value Change");

                    _graphView.AddElement(node);
                    nodeDict.Add(node.GUID, node);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to load node {nodeData.Title}: {ex}");
                }
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

        private void CreatePreviewPanel()
        {
            _previewContainer = new VisualElement();
            _previewContainer.name = "PreviewPanel";
            _previewContainer.style.position = Position.Absolute;
            _previewContainer.style.bottom = 0;
            _previewContainer.style.left = 0;
            _previewContainer.style.right = 0;
            _previewContainer.style.height = 250; //稍微加高一点
            _previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            _previewContainer.style.borderTopWidth = 2;
            _previewContainer.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            _previewContainer.visible = false;

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.height = 28;
            header.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            header.style.paddingLeft = 10;
            header.style.alignItems = Align.Center;

            var titleLabel = new Label("Preview Results");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1;

            var copyBtn = new Button(() =>
            {
                if (_previewContext != null)
                {
                    // 1. 生成文本
                    string content = GeneratePreviewText(_previewContext);

                    // 2. 【关键修复】使用 GUIUtility 直接写入系统剪贴板
                    GUIUtility.systemCopyBuffer = content;

                    // 3. 打印日志 (并在 Log 里显示一部分内容确认)
                    string shortText = content.Length > 50 ? content.Substring(0, 50) + "..." : content;
                    Debug.Log($"[BuildGraph] Copied to clipboard ({content.Length} chars):\n{shortText}");
                }
                else
                {
                    Debug.LogWarning("[BuildGraph] Nothing to copy (Context is null)");
                }
            })
            { text = "Copy All" };

            copyBtn.style.marginRight = 10;

            var closeBtn = new Button(() => { _previewContainer.visible = false; }) { text = "X" };
            closeBtn.style.width = 25;
            closeBtn.style.flexShrink = 0; // 防止压缩

            header.Add(titleLabel);
            header.Add(copyBtn); // 加在这里
            header.Add(closeBtn);

            // Scroll View
            _previewScrollView = new ScrollView();
            _previewScrollView.style.flexGrow = 1; // 填满剩余空间

            _previewContainer.Add(header);
            _previewContainer.Add(_previewScrollView);
            rootVisualElement.Add(_previewContainer);
        }
        private string GeneratePreviewText(BuildContext context)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Part 1: Logs
            if (context.Logs.Length > 0)
            {
                sb.AppendLine("=== EXECUTION LOGS ===");
                sb.AppendLine(context.Logs.ToString());
                sb.AppendLine();
            }

            // Part 2: Assets
            sb.AppendLine($"=== OUTPUT ASSETS ({context.Assets.Count}) ===");
            if (context.Assets.Count == 0)
            {
                sb.AppendLine("(List is empty)");
            }
            else
            {
                // 按 BundleName 分组显示，更清晰
                var groups = context.Assets.GroupBy(a => string.IsNullOrEmpty(a.BundleName) ? "[No Bundle]" : a.BundleName);

                foreach (var g in groups)
                {
                    sb.AppendLine($"Bundle: {g.Key}");
                    foreach (var asset in g)
                    {
                        sb.AppendLine($"  - {asset.AssetPath}");
                    }
                    sb.AppendLine(); // 组间空行
                }
            }

            return sb.ToString();
        }
        private TextField CreateSelectableLabel(string text, Color color, bool bold = false)
        {
            var field = new TextField();
            field.value = text;
            field.isReadOnly = true; // 只读
            field.multiline = true;  // 支持多行 (针对 Log)

            // 样式调整：去掉输入框的边框和背景，让它看起来像 Label
            field.style.backgroundColor = new Color(0, 0, 0, 0);
            field.style.borderTopWidth = 0;
            field.style.borderBottomWidth = 0;
            field.style.borderLeftWidth = 0;
            field.style.borderRightWidth = 0;

            // 字体样式
            field.style.color = color;
            if (bold) field.style.unityFontStyleAndWeight = FontStyle.Bold;

            // 调整 Margin 使其紧凑
            field.style.marginTop = 0;
            field.style.marginBottom = 2;
            field.style.marginLeft = 2;

            // 允许文本换行
            field.style.whiteSpace = WhiteSpace.Normal;

            return field;
        }

        private void UpdatePreviewPanel(BuildContext context)
        {
            _previewScrollView.Clear();
            _previewContext = context;
            if (context == null)
            {
                _previewContainer.visible = false;
                return;
            }

            _previewContainer.visible = true;

            // 1. 显示日志
            if (context.Logs.Length > 0)
            {
                _previewScrollView.Add(new Label("Execution Logs:")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.7f, 0.7f, 0.7f) }
                });

                // 使用可复制的 TextField 显示日志
                var logField = CreateSelectableLabel(context.Logs.ToString(), Color.white);
                logField.style.marginBottom = 15;
                _previewScrollView.Add(logField);
            }

            // 2. 显示资产
            _previewScrollView.Add(new Label($"Assets Output ({context.Assets.Count}):")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.4f, 0.8f, 0.4f) }
            });

            if (context.Assets.Count == 0)
            {
                _previewScrollView.Add(new Label("  (Empty List)") { style = { color = Color.gray } });
            }
            else
            {
                foreach (var asset in context.Assets)
                {
                    string bundleText = string.IsNullOrEmpty(asset.BundleName) ? "[No Bundle]" : $"[{asset.BundleName}]";
                    string fullText = $"  {bundleText}  {asset.AssetPath}";

                    Color textColor = string.IsNullOrEmpty(asset.BundleName) ? Color.white : new Color(0.8f, 0.8f, 1f);

                    // 使用可复制的 TextField 显示资产行
                    _previewScrollView.Add(CreateSelectableLabel(fullText, textColor));
                }
            }
        }

        private void PreviewSelectedNode()
        {
            if (_graphView.selection.Count > 0 && _graphView.selection[0] is BaseBuildNode selectedNode)
            {
                Debug.Log($"[BuildGraph] Previewing: {selectedNode.title}");
                // 获取图中所有节点传给 Runner
                var allNodes = _graphView.nodes.ToList().Cast<BaseBuildNode>().ToList();

                var context = GraphRunner.Run(selectedNode, allNodes);
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