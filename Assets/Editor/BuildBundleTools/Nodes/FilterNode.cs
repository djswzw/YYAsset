using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    // 过滤类型枚举
    public enum FilterType
    {
        Extension,  // 后缀名 (e.g., .prefab)
        PathRegex,  // 路径正则
        FileStartWith, // 文件名开头
    }

    [System.Serializable]
    public class FilterRule
    {
        public bool IsEnabled = true;
        public FilterType Type = FilterType.Extension;
        public string Keyword = "";
        public string PortName = "Output"; // 对应输出端口名
    }

    public class FilterNode : BaseBuildNode
    {
        // 规则列表
        public List<FilterRule> Rules = new List<FilterRule>();
        // 默认端口名 (未匹配的资产走这里)
        private const string kDefaultPort = "Fallback";

        public override void Initialize()
        {
            title = "Advanced Filter";

            // 1. 静态端口
            AddInputPort("Input");

            // 2. 绘制 UI
            DrawUI();

            // 3. 刷新动态端口
            RefreshDynamicPorts();
        }

        private void DrawUI()
        {
            // 清理旧 UI (除端口外)
            var existingUI = mainContainer.Query<VisualElement>("custom-ui").First();
            if (existingUI != null) mainContainer.Remove(existingUI);

            var container = new VisualElement { name = "custom-ui" };
            container.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            container.style.paddingTop = 5;
            container.style.paddingBottom = 5;

            // 添加规则按钮
            var addBtn = new Button(() =>
            {
                Rules.Add(new FilterRule { PortName = $"Rule {Rules.Count + 1}", Keyword = ".prefab" });
                RefreshDynamicPorts();
                DrawUI(); // 重绘列表
                NotifyChange();
            })
            { text = "+ Add Rule" };
            container.Add(addBtn);

            // 规则列表
            for (int i = 0; i < Rules.Count; i++)
            {
                int index = i;
                var rule = Rules[i];
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };

                // Enable Toggle
                var toggle = new Toggle { value = rule.IsEnabled };
                toggle.RegisterValueChangedCallback(e => { rule.IsEnabled = e.newValue; NotifyChange(); });

                // Type Enum
                var typeField = new EnumField(rule.Type) { style = { width = 80 } };
                typeField.RegisterValueChangedCallback(e => rule.Type = (FilterType)e.newValue);

                // Keyword Text
                var kwField = new TextField { value = rule.Keyword, style = { flexGrow = 1 } };
                kwField.RegisterValueChangedCallback(e => { rule.Keyword = e.newValue; NotifyChange(); });

                // Delete Btn
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
                container.Add(row);
            }

            mainContainer.Add(container);
        }

        private void RefreshDynamicPorts()
        {
            ClearOutputPorts(); // 清空旧端口

            // 为每个规则生成一个端口
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled) AddOutputPort(rule.PortName);
            }

            // 添加兜底端口
            AddOutputPort(kDefaultPort);
        }

        // --- 执行逻辑 ---
        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[FilterNode] Processing {context.Assets.Count} assets with {Rules.Count} rules.");

            var outputs = new Dictionary<string, BuildContext>();

            // 初始化所有输出上下文
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled && !outputs.ContainsKey(rule.PortName))
                    outputs[rule.PortName] = new BuildContext();
            }
            outputs[kDefaultPort] = new BuildContext();

            // 遍历资产进行分拣
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
                        break; // 匹配到第一条规则就拿走 (互斥)
                    }
                }

                if (!matched)
                {
                    outputs[kDefaultPort].Assets.Add(asset);
                }
            }

            // 记录日志
            foreach (var kvp in outputs)
            {
                if (kvp.Value.Assets.Count > 0)
                    context.Logs.AppendLine($"  -> Port '{kvp.Key}': {kvp.Value.Assets.Count} assets");
            }

            return outputs;
        }

        private bool CheckMatch(string path, FilterRule rule)
        {
            string name = System.IO.Path.GetFileName(path);
            switch (rule.Type)
            {
                case FilterType.Extension:
                    return path.EndsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);
                case FilterType.FileStartWith:
                    return name.StartsWith(rule.Keyword, System.StringComparison.OrdinalIgnoreCase);
                case FilterType.PathRegex:
                    return Regex.IsMatch(path, rule.Keyword);
                default: return false;
            }
        }

        // --- 序列化 ---
        [System.Serializable]
        class NodeData { public List<FilterRule> rules; }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData { rules = Rules });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null && data.rules != null)
            {
                Rules = data.rules;
                RefreshDynamicPorts(); // 恢复端口
                DrawUI(); // 恢复 UI
            }
        }
    }
}