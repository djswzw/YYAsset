using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;

namespace YY.Build.Graph.Nodes
{
    public class MergeNode : BaseBuildNode
    {
        public override void Initialize()
        {
            title = "Process: Merge";

            // 允许多个输入汇聚
            AddInputPort("Input", Port.Capacity.Multi);
            // 单一输出
            AddOutputPort("Output", Port.Capacity.Multi);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // GraphRunner 会自动把所有连入 "Input" 的上游数据合并到 context 中
            // 所以这里只需要简单透传即可
            context.Logs.AppendLine($"[MergeNode] Merged {context.Assets.Count} assets.");
            return base.Execute(context);
        }

        // 记得实现序列化，虽然没有字段，但为了 Undo/Load 稳定性
        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => UnityEngine.JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }
}