using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using YY.Build.Graph;
using YY.Build.Graph.Nodes; // 引用 Portal 节点

namespace YY.Build.Core
{
    public static class GraphRunner
    {
        public static Dictionary<string, Dictionary<string, BuildContext>> DataMap;

        // 【修改】Editor 模式入口：增加 allNodes 参数
        public static BuildContext Run(BaseBuildNode startNode, List<BaseBuildNode> allNodes)
        {
            // 1. 建立常规的 UI 连线关系
            // 我们需要对所有节点都建立关系，不仅仅是 startNode，因为 Sender 可能在别处
            foreach (var node in allNodes)
            {
                PrepareConnectionsFromUI(node);
            }

            // 2. 【核心】建立 Portal 的无线连接
            LinkPortals(allNodes);

            // 3. 执行
            return RunInternal(startNode);
        }

        // Headless 模式入口 (假设外部已经处理好了所有连接，包括 Portal)
        public static BuildContext RunHeadless(BaseBuildNode startNode)
        {
            return RunInternal(startNode);
        }

        // --- Portal 连接逻辑 ---
        private static void LinkPortals(List<BaseBuildNode> allNodes)
        {
            // 1. 搜集所有 Sender
            var senderMap = new Dictionary<string, PortalSenderNode>();
            foreach (var node in allNodes)
            {
                if (node is PortalSenderNode sender && !string.IsNullOrEmpty(sender.PortalID))
                {
                    if (!senderMap.ContainsKey(sender.PortalID))
                        senderMap[sender.PortalID] = sender;
                    else
                        UnityEngine.Debug.LogWarning($"[GraphRunner] Duplicate Portal ID found: {sender.PortalID}. Using the first one.");
                }
            }

            // 2. 连接所有 Receiver
            foreach (var node in allNodes)
            {
                if (node is PortalReceiverNode receiver && !string.IsNullOrEmpty(receiver.PortalID))
                {
                    if (senderMap.TryGetValue(receiver.PortalID, out var sender))
                    {
                        // 【魔法时刻】注入逻辑连接
                        // Receiver 的 "VirtualInput" 端口 <--- Sender 的 "Output" 端口
                        // 这样拓扑排序时，Receiver 就会依赖 Sender
                        receiver.AddUpstreamConnection("VirtualInput", sender, "Output");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[GraphRunner] Receiver '{receiver.title}' cannot find Sender with ID: {receiver.PortalID}");
                    }
                }
            }
        }

        private static BuildContext RunInternal(BaseBuildNode startNode)
        {
            DataMap = new Dictionary<string, Dictionary<string, BuildContext>>();

            // 1. 拓扑排序
            var sortedNodes = TopologicalSort(startNode);

            // 2. 顺序执行
            foreach (var node in sortedNodes)
            {
                // 【核心修复】自动聚合上游数据
                var inputContext = new BuildContext();

                // 获取我们在 PrepareConnectionsFromUI 阶段注入的逻辑连接
                var connections = node.GetUpstreamConnections();

                if (connections != null)
                {
                    foreach (var portEntry in connections)
                    {
                        // portEntry.Key 是本节点的 Input 端口名 (如 "Input", "Source", "Reserved")
                        // portEntry.Value 是上游节点列表
                        foreach (var (upstreamNode, upstreamPortName) in portEntry.Value)
                        {
                            // 从全局缓存中查找上游节点的输出数据
                            if (DataMap.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                                nodeOutputs.TryGetValue(upstreamPortName, out var upstreamData))
                            {
                                // 合并数据到 InputContext
                                // 注意：这里是把所有端口的数据都混在了一起传给 Execute
                                // 对于 Filter/Grouper 这种单路输入的节点，这完全正确。
                                // 对于 Deduplicator 这种多路输入的节点，它会忽略这个 inputContext，
                                // 转而使用 GetInputContext("Source") 手动去 DataMap 拉取区分后的数据。
                                inputContext.Assets.AddRange(upstreamData.Assets);
                                if (upstreamData.Logs.Length > 0)
                                    inputContext.Logs.AppendLine(upstreamData.Logs.ToString());
                            }
                        }
                    }
                }

                // 执行节点逻辑
                var outputs = node.Execute(inputContext);

                // 缓存输出结果
                DataMap[node.GUID] = outputs;
            }

            // 3. 返回最终结果 (用于预览)
            if (DataMap.TryGetValue(startNode.GUID, out var finalOutputs))
            {
                var preview = new BuildContext();
                // 优先返回 Output 端口
                if (finalOutputs.ContainsKey("Output")) return finalOutputs["Output"];
                // 否则合并所有输出端口 (比如 Filter 有多个 Output)
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

        // 修改：现在 Prepare 只需要处理单个节点，不需要递归 visited，因为我们在外层遍历了
        private static void PrepareConnectionsFromUI(BaseBuildNode node)
        {
            node.ResetConnections();

            // 遍历 UI 输入端口
            foreach (var element in node.inputContainer.Children())
            {
                if (element is Port inPort && inPort.connected)
                {
                    foreach (var edge in inPort.connections)
                    {
                        var upstreamNode = edge.output.node as BaseBuildNode;
                        if (upstreamNode != null)
                        {
                            node.AddUpstreamConnection(inPort.portName, upstreamNode, edge.output.portName);
                        }
                    }
                }
            }
        }

        // TopologicalSort 保持不变
        private static List<BaseBuildNode> TopologicalSort(BaseBuildNode startNode)
        {
            var sorted = new List<BaseBuildNode>();
            var visited = new HashSet<BaseBuildNode>();
            var recursionStack = new HashSet<BaseBuildNode>();

            void Visit(BaseBuildNode node)
            {
                if (recursionStack.Contains(node)) return;
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