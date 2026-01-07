using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using YY.Build.Graph;

namespace YY.Build.Core
{
    public static class GraphRunner
    {
        // 【核心修复】使用 Stack 支持递归/子图嵌套
        // 每一层递归（主图 -> 子图 -> 子子图）都有自己独立的 DataMap
        private static Stack<Dictionary<string, Dictionary<string, BuildContext>>> _dataMapStack
            = new Stack<Dictionary<string, Dictionary<string, BuildContext>>>();

        // 获取当前正在执行的那一层的 DataMap
        public static Dictionary<string, Dictionary<string, BuildContext>> CurrentDataMap
        {
            get
            {
                if (_dataMapStack.Count > 0) return _dataMapStack.Peek();
                return null;
            }
        }

        // Editor 模式入口
        public static BuildContext Run(BaseBuildNode startNode, List<BaseBuildNode> allNodes, bool isBuildMode = false)
        {
            _dataMapStack.Clear();
            foreach (var node in allNodes) PrepareConnectionsFromUI(node);
            LinkPortals(allNodes);
            return RunInternal(startNode, isBuildMode);
        }


        // Headless 模式入口
        public static BuildContext RunHeadless(BaseBuildNode startNode, bool isBuildMode = true)
        {
            // Headless 默认就是为了打包，所以默认为 true
            return RunInternal(startNode, isBuildMode);
        }

        // 通用执行逻辑
        private static BuildContext RunInternal(BaseBuildNode startNode, bool isBuildMode)
        {
            _dataMapStack.Push(new Dictionary<string, Dictionary<string, BuildContext>>());

            try
            {
                var sortedNodes = TopologicalSort(startNode);

                foreach (var node in sortedNodes)
                {
                    // 1. 聚合上游数据
                    var inputContext = new BuildContext();
                    var connections = node.GetUpstreamConnections();

                    if (connections != null)
                    {
                        foreach (var portEntry in connections)
                        {
                            foreach (var (upstreamNode, upstreamPortName) in portEntry.Value)
                            {
                                // 使用 CurrentDataMap 获取当前层的数据
                                if (CurrentDataMap.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                                    nodeOutputs.TryGetValue(upstreamPortName, out var upstreamData))
                                {
                                    inputContext.Assets.AddRange(upstreamData.Assets);
                                    if (upstreamData.Logs.Length > 0)
                                        inputContext.Logs.AppendLine(upstreamData.Logs.ToString());
                                }
                            }
                        }
                    }
                    inputContext.IsBuildMode = isBuildMode;
                    // 2. 执行节点
                    var outputs = node.Execute(inputContext);

                    // 3. 写入当前层的缓存
                    CurrentDataMap[node.GUID] = outputs;
                }

                // 4. 获取结果
                if (CurrentDataMap.TryGetValue(startNode.GUID, out var finalOutputs))
                {
                    var result = new BuildContext();
                    if (finalOutputs.ContainsKey("Output")) result = finalOutputs["Output"];
                    else
                    {
                        foreach (var kvp in finalOutputs)
                        {
                            result.Assets.AddRange(kvp.Value.Assets);
                            result.Logs.AppendLine($"--- Port: {kvp.Key} ---");
                            result.Logs.Append(kvp.Value.Logs);
                        }
                    }
                    return result;
                }

                return new BuildContext();
            }
            finally
            {
                _dataMapStack.Pop();
            }
        }

        // --- 辅助方法 (保持不变) ---
        private static void LinkPortals(List<BaseBuildNode> allNodes)
        {
            var senderMap = new Dictionary<string, YY.Build.Graph.Nodes.PortalSenderNode>();
            foreach (var node in allNodes)
                if (node is YY.Build.Graph.Nodes.PortalSenderNode s && !string.IsNullOrEmpty(s.PortalID))
                    senderMap[s.PortalID] = s;

            foreach (var node in allNodes)
                if (node is YY.Build.Graph.Nodes.PortalReceiverNode r && !string.IsNullOrEmpty(r.PortalID))
                    if (senderMap.TryGetValue(r.PortalID, out var s)) r.AddUpstreamConnection("VirtualInput", s, "Output");
        }

        private static void PrepareConnectionsFromUI(BaseBuildNode node)
        {
            node.ResetConnections();
            foreach (var element in node.inputContainer.Children())
            {
                if (element is Port inPort && inPort.connected)
                {
                    foreach (var edge in inPort.connections)
                    {
                        var upstreamNode = edge.output.node as BaseBuildNode;
                        if (upstreamNode != null) node.AddUpstreamConnection(inPort.portName, upstreamNode, edge.output.portName);
                    }
                }
            }
        }

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