using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using YY.Build.Graph;

namespace YY.Build.Core
{
    public static class GraphRunner
    {
        // 存储每个节点每个端口输出的数据
        // Key: NodeGUID, Value: { PortName -> Context }
        private static Dictionary<string, Dictionary<string, BuildContext>> _dataMap;

        public static BuildContext Run(BaseBuildNode startNode)
        {
            _dataMap = new Dictionary<string, Dictionary<string, BuildContext>>();

            // 1. 拓扑排序 (确保依赖顺序)
            var sortedNodes = TopologicalSort(startNode);

            // 2. 依次执行
            foreach (var node in sortedNodes)
            {
                // A. 收集输入数据 (Merge Inputs)
                var inputContext = new BuildContext();

                // 遍历该节点的所有输入端口
                foreach (var child in node.inputContainer.Children())
                {
                    if (child is Port inPort && inPort.connected)
                    {
                        foreach (var edge in inPort.connections)
                        {
                            var upstreamNode = edge.output.node as BaseBuildNode;
                            var upstreamPortName = edge.output.portName;

                            // 从缓存中查找上游节点该端口输出的数据
                            if (_dataMap.ContainsKey(upstreamNode.GUID) &&
                                _dataMap[upstreamNode.GUID].TryGetValue(upstreamPortName, out var upstreamData))
                            {
                                // 合并数据
                                inputContext.Assets.AddRange(upstreamData.Assets);
                                if (upstreamData.Logs.Length > 0) inputContext.Logs.AppendLine(upstreamData.Logs.ToString());
                            }
                        }
                    }
                }

                // B. 执行节点逻辑，获取多端口输出
                // 如果是源节点(没有输入)，Execute 内部会负责生成初始数据
                var outputs = node.Execute(inputContext);

                // C. 缓存输出结果
                _dataMap[node.GUID] = outputs;
            }

            // 返回选中节点 "Output" 端口的数据作为预览，如果没有 Output 端口则合并所有端口
            if (_dataMap.TryGetValue(startNode.GUID, out var finalOutputs))
            {
                // 如果是为了预览，我们聚合所有输出
                var preview = new BuildContext();
                if (finalOutputs.ContainsKey("Output")) return finalOutputs["Output"];

                foreach (var kvp in finalOutputs)
                {
                    preview.Assets.AddRange(kvp.Value.Assets);
                    preview.Logs.AppendLine($"--- Port: {kvp.Key} ---");
                    preview.Logs.Append(kvp.Value.Logs);
                }
                return preview;
            }

            return new BuildContext();
        }

        // 拓扑排序保持不变... (省略，与上一章相同，请保留原有的 TopologicalSort)
        private static List<BaseBuildNode> TopologicalSort(BaseBuildNode startNode)
        {
            var sorted = new List<BaseBuildNode>();
            var visited = new HashSet<BaseBuildNode>();
            var recursionStack = new HashSet<BaseBuildNode>();

            void Visit(BaseBuildNode node)
            {
                if (recursionStack.Contains(node)) return; // 简单防环
                if (visited.Contains(node)) return;

                visited.Add(node);
                recursionStack.Add(node);

                foreach (var inputNode in node.GetInputNodes()) Visit(inputNode);

                recursionStack.Remove(node);
                sorted.Add(node);
            }

            Visit(startNode);
            return sorted;
        }
    }
}