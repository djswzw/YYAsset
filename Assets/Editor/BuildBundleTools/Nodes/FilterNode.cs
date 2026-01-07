using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    public enum FilterType
    {
        Extension,          // 后缀名 (.prefab)
        DirectoryStartsWith,// 目录开头 (Assets/Res/UI/)
        PathRegex,          // 正则
        FileStartWith       // 文件名开头
    }

    [System.Serializable]
    public class FilterRule
    {
        public bool IsEnabled = true;
        public FilterType Type = FilterType.Extension;
        public string Keyword = "";
        public string PortName = "Output";
    }

    public class FilterNode : BaseBuildNode
    {
        public List<FilterRule> Rules = new List<FilterRule>();
        private const string kDefaultPort = "Fallback";
        private VisualElement _uiContainer;

        public override void Initialize()
        {
            title = "Advanced Filter";
            base.Initialize();
            // 端口
            AddInputPort("Input", Port.Capacity.Multi);
            DrawUI();
            RefreshDynamicPorts();
        }

        private void DrawUI()
        {
            if (_uiContainer != null) mainContainer.Remove(_uiContainer);
            _uiContainer = new VisualElement { style = { backgroundColor = new Color(0.25f, 0.25f, 0.25f), paddingTop = 5, paddingBottom = 5 } };

            // 添加按钮
            var addBtn = new Button(() =>
            {
                // 默认添加一个目录规则
                Rules.Add(new FilterRule { PortName = $"Rule {Rules.Count + 1}", Type = FilterType.DirectoryStartsWith, Keyword = "Assets/" });
                RefreshDynamicPorts();
                DrawUI();
                NotifyChange();
            })
            { text = "+ Add Rule" };
            _uiContainer.Add(addBtn);

            // 绘制每一行规则
            for (int i = 0; i < Rules.Count; i++)
            {
                int index = i;
                var rule = Rules[i];
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, alignItems = Align.Center } };

                // 1. 开关
                var toggle = new Toggle { value = rule.IsEnabled, style = { width = 15 } };
                toggle.RegisterValueChangedCallback(e => { rule.IsEnabled = e.newValue; NotifyChange(); });

                // 2. 类型下拉框
                var typeField = new EnumField(rule.Type) { style = { width = 85, marginRight = 2 } };
                typeField.RegisterValueChangedCallback(e =>
                {
                    rule.Type = (FilterType)e.newValue;
                    // 类型改变时，如果是 Directory 且关键字为空，给个默认值
                    if (rule.Type == FilterType.DirectoryStartsWith && string.IsNullOrEmpty(rule.Keyword)) rule.Keyword = "Assets/";
                    DrawUI(); // 重绘本行以切换输入控件
                    NotifyChange();
                });

                // 3. 动态输入区域 (核心优化)
                VisualElement inputContainer = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } };

                TextField kwField = new TextField { value = rule.Keyword, style = { flexGrow = 1 } };
                kwField.RegisterValueChangedCallback(e => { rule.Keyword = e.newValue; NotifyChange(); });

                // 如果是目录模式，增加“浏览”和“拖拽”功能
                if (rule.Type == FilterType.DirectoryStartsWith)
                {
                    // 启用拖拽支持
                    kwField.RegisterCallback<DragUpdatedEvent>(evt =>
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    });
                    kwField.RegisterCallback<DragPerformEvent>(evt =>
                    {
                        DragAndDrop.AcceptDrag();
                        if (DragAndDrop.paths.Length > 0)
                        {
                            string path = DragAndDrop.paths[0]; // 获取拖入的路径
                            if (System.IO.Directory.Exists(path)) // 确保是文件夹
                            {
                                rule.Keyword = path + "/"; // 自动补齐末尾斜杠
                                kwField.value = rule.Keyword;
                                NotifyChange();
                            }
                        }
                    });
                    // 样式提示
                    kwField.tooltip = "Drag a folder here";

                    // 浏览按钮
                    var browseBtn = new Button(() =>
                    {
                        string path = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            int idx = path.IndexOf("Assets");
                            if (idx >= 0)
                            {
                                rule.Keyword = path.Substring(idx) + "/";
                                kwField.value = rule.Keyword;
                                NotifyChange();
                            }
                        }
                    })
                    { text = "...", style = { width = 20 } };

                    inputContainer.Add(kwField);
                    inputContainer.Add(browseBtn);
                }
                else
                {
                    // 普通文本模式
                    inputContainer.Add(kwField);
                }

                // 4. 删除按钮
                var delBtn = new Button(() =>
                {
                    Rules.RemoveAt(index);
                    RefreshDynamicPorts();
                    DrawUI();
                    NotifyChange();
                })
                { text = "X", style = { color = Color.red, width = 20 } };

                row.Add(toggle);
                row.Add(typeField);
                row.Add(inputContainer); // 加入动态容器
                row.Add(delBtn);
                _uiContainer.Add(row);
            }
            mainContainer.Add(_uiContainer);
        }

        private void RefreshDynamicPorts()
        {
            // 记录连接并恢复 (保持之前的逻辑)
            var connectionsToRestore = new List<(string, Port)>();
            foreach (var element in outputContainer.Children())
            {
                if (element is Port port && port.connected)
                    foreach (var edge in port.connections) if (edge.input != null) connectionsToRestore.Add((port.portName, edge.input));
            }

            ClearOutputPorts();

            foreach (var rule in Rules) if (rule.IsEnabled) AddOutputPort(rule.PortName, Port.Capacity.Multi);
            AddOutputPort(kDefaultPort, Port.Capacity.Multi);

            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            if (graphView != null)
            {
                foreach (var record in connectionsToRestore)
                {
                    var newOutPort = outputContainer.Q<Port>(record.Item1);
                    if (newOutPort != null && record.Item2 != null && record.Item2.node != null)
                        graphView.AddElement(newOutPort.ConnectTo(record.Item2));
                }
            }
        }

        // --- 执行逻辑 ---
        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            var outputs = new Dictionary<string, BuildContext>();
            foreach (var rule in Rules) if (rule.IsEnabled) outputs[rule.PortName] = new BuildContext();
            outputs[kDefaultPort] = new BuildContext();

            foreach (var asset in context.Assets)
            {
                bool matched = false;
                foreach (var rule in Rules)
                {
                    if (!rule.IsEnabled) continue;
                    if (CheckMatch(asset.AssetPath, rule))
                    {
                        outputs[rule.PortName].Assets.Add(asset);
                        matched = true;
                        break;
                    }
                }
                if (!matched) outputs[kDefaultPort].Assets.Add(asset);
            }
            return outputs;
        }

        private bool CheckMatch(string path, FilterRule rule)
        {
            // 统一路径分隔符，确保 StartsWith 匹配准确
            path = path.Replace("\\", "/");

            switch (rule.Type)
            {
                case FilterType.Extension:
                    return path.EndsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);

                case FilterType.DirectoryStartsWith:
                    // 核心优化：直接字符串匹配，性能极快
                    // 确保 Keyword 也是标准路径格式
                    string keyword = rule.Keyword.Replace("\\", "/");
                    return path.StartsWith(keyword, System.StringComparison.OrdinalIgnoreCase);

                case FilterType.FileStartWith:
                    return System.IO.Path.GetFileName(path).StartsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);

                case FilterType.PathRegex:
                    return Regex.IsMatch(path, rule.Keyword);

                default: return false;
            }
        }

        // 序列化
        [System.Serializable] class NodeData { public List<FilterRule> rules; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { rules = Rules });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { Rules = data.rules ?? new List<FilterRule>(); RefreshDynamicPorts(); DrawUI(); }
        }
    }
}