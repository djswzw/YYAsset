using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;
using YY.Build.Windows;

namespace YY.Build.Graph.Nodes
{
    public class ViewerNode : BaseBuildNode
    {
        public override void Initialize()
        {
            base.Initialize();
            title = "Debug: Asset Viewer";

            // 1. 端口配置：多入多出，实现透传
            AddInputPort("Input", Port.Capacity.Multi);
            AddOutputPort("Output", Port.Capacity.Multi);

            // 2. 样式：给个特殊的颜色区分
            titleContainer.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f);

            // 3. UI 按钮
            var container = new VisualElement();

            var openBtn = new Button(OnOpenClick)
            {
                text = "Inspect Assets 🔍",
                style = { height = 25 }
            };

            var desc = new Label("Pass-through node.\nClick to view asset list.");
            desc.style.fontSize = 10;
            desc.style.color = Color.gray;

            container.Add(openBtn);
            container.Add(desc);
            mainContainer.Add(container);
        }

        private void OnOpenClick()
        {
            // 1. 获取 GraphView 和所有节点
            var graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView == null) return;

            var allNodes = graphView.nodes.ToList().Cast<BaseBuildNode>().ToList();

            // 2. 运行 GraphRunner 到当前节点
            Debug.Log($"[ViewerNode] Calculating assets up to '{title}'...");
            var context = GraphRunner.Run(this, allNodes);

            // 3. 打开窗口显示结果
            AssetListWindow.Open(context.Assets, $"Viewer: {title}");
        }

        // --- 透传逻辑 ---
        // 它的 Execute 不做任何修改，仅仅把输入传给输出
        // 这样你可以把它插入到任何连线的中间，而不破坏原有逻辑
        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // context 已经被 GraphRunner 填充了上游数据，直接返回即可
            context.Logs.AppendLine($"[ViewerNode] Passed through {context.Assets.Count} assets.");
            return base.Execute(context);
        }

        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }
}