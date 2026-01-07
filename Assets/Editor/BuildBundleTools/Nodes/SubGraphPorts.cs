using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;

namespace YY.Build.Graph.Nodes
{
    // --- 子图入口 ---
    // 放在子图里，充当数据源头
    public class SubGraphInputNode : BaseBuildNode
    {
        // 用于在运行时接收父图注入的数据
        public BuildContext InjectedContext;

        public override void Initialize()
        {
            base.Initialize();
            title = "SubGraph: Input";
            // 只有输出，没有输入（因为它的输入来自父图，是隐式的）
            AddOutputPort("Output", Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new UnityEngine.Color(0.2f, 0.5f, 0.5f); // 青色背景
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 如果有外部注入的数据，就使用注入的；否则使用空的（用于单独调试子图时）
            var result = InjectedContext ?? new BuildContext();

            // 记录日志，方便调试
            result.Logs.AppendLine("[SubGraph Input] Received data from Parent.");

            return new Dictionary<string, BuildContext> { { "Output", result } };
        }

        // 序列化
        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => UnityEngine.JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }

    // --- 子图出口 ---
    // 放在子图里，作为终点
    public class SubGraphOutputNode : BaseBuildNode
    {
        public override void Initialize()
        {
            base.Initialize();
            title = "SubGraph: Output";
            // 只有输入
            AddInputPort("Input", Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new UnityEngine.Color(0.5f, 0.2f, 0.5f); // 紫色背景
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 直接透传数据，GraphRunner 会捕获它的结果
            context.Logs.AppendLine("[SubGraph Output] Sending data to Parent.");
            return base.Execute(context);
        }

        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => UnityEngine.JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }
}