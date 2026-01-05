using System.Linq;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView; // 必须引用
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    // FilterType 和 FilterRule 类保持不变...
    public enum FilterType { Extension, PathRegex, FileStartWith }

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
            AddInputPort("Input", Port.Capacity.Multi);
            DrawUI();
            RefreshDynamicPorts();
        }

        private void DrawUI()
        {
            if (_uiContainer != null) mainContainer.Remove(_uiContainer);
            _uiContainer = new VisualElement { style = { backgroundColor = new Color(0.25f, 0.25f, 0.25f), paddingTop = 5, paddingBottom = 5 } };

            var addBtn = new Button(() =>
            {
                Rules.Add(new FilterRule { PortName = $"Rule {Rules.Count + 1}", Keyword = ".prefab" });
                RefreshDynamicPorts();
                DrawUI();
                NotifyChange();
            })
            { text = "+ Add Rule" };
            _uiContainer.Add(addBtn);

            for (int i = 0; i < Rules.Count; i++)
            {
                int index = i;
                var rule = Rules[i];
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };

                var toggle = new Toggle { value = rule.IsEnabled };
                toggle.RegisterValueChangedCallback(e => { rule.IsEnabled = e.newValue; NotifyChange(); });

                var typeField = new EnumField(rule.Type) { style = { width = 80 } };
                typeField.RegisterValueChangedCallback(e => { rule.Type = (FilterType)e.newValue; NotifyChange(); });

                var kwField = new TextField { value = rule.Keyword, style = { flexGrow = 1 } };
                kwField.RegisterValueChangedCallback(e => { rule.Keyword = e.newValue; NotifyChange(); });

                var delBtn = new Button(() =>
                {
                    Rules.RemoveAt(index);
                    RefreshDynamicPorts();
                    DrawUI();
                    NotifyChange();
                })
                { text = "X", style = { color = Color.red } };

                row.Add(toggle);
                row.Add(typeField);
                row.Add(kwField);
                row.Add(delBtn);
                _uiContainer.Add(row);
            }
            mainContainer.Add(_uiContainer);
        }

        // --- 【关键修复】刷新端口并保持连线 ---
        private void RefreshDynamicPorts()
        {
            // 1. 记录现有的连接信息
            // 结构: (本节点端口名, 目标端口对象)
            var connectionsToRestore = new List<(string OutPortName, Port TargetPort)>();

            foreach (var element in outputContainer.Children())
            {
                if (element is Port port && port.connected)
                {
                    foreach (var edge in port.connections)
                    {
                        if (edge.input != null)
                        {
                            connectionsToRestore.Add((port.portName, edge.input));
                        }
                    }
                }
            }

            // 2. 清除旧端口 (BaseBuildNode 中的方法会物理断开 Edge)
            ClearOutputPorts();

            // 3. 生成新端口
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled) AddOutputPort(rule.PortName, Port.Capacity.Multi);
            }
            AddOutputPort(kDefaultPort, Port.Capacity.Multi);

            // 4. 恢复连接
            var graphView = GetFirstAncestorOfType<GraphView>();

            if (graphView != null)
            {
                foreach (var record in connectionsToRestore)
                {
                    // 在新生成的端口中查找同名的
                    var newOutPort = outputContainer.Q<Port>(record.OutPortName);
                    var targetInPort = record.TargetPort;

                    // 只有当双方都存在且有效时才重连
                    if (newOutPort != null && targetInPort != null && targetInPort.node != null)
                    {
                        var newEdge = newOutPort.ConnectTo(targetInPort);
                        graphView.AddElement(newEdge);
                    }
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
            string name = System.IO.Path.GetFileName(path);
            switch (rule.Type)
            {
                case FilterType.Extension: return path.EndsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);
                case FilterType.FileStartWith: return name.StartsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);
                case FilterType.PathRegex: return System.Text.RegularExpressions.Regex.IsMatch(path, rule.Keyword);
                default: return false;
            }
        }

        // --- 序列化 ---
        [System.Serializable] class NodeData { public List<FilterRule> rules; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { rules = Rules });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { Rules = data.rules ?? new List<FilterRule>(); RefreshDynamicPorts(); DrawUI(); }
        }
    }
}