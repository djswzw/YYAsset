using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YY.Build.Data;
using YY.Build.Graph;
using YY.Build.Graph.Nodes;

namespace YY.Build.Core
{
    public static class HeadlessBuilder
    {
        /// <summary>
        /// 自动化打包入口
        /// </summary>
        /// <param name="graphAssetPath">Graph 资产路径</param>
        /// <param name="targetNodeName">要执行的打包节点名称 (支持模糊匹配，默认查找BatchBuildNode)</param>
        public static void Build(string graphAssetPath, string targetNodeName = "Batch")
        {
            // 1. 加载 Asset
            var graphAsset = AssetDatabase.LoadAssetAtPath<BuildGraphAsset>(graphAssetPath);
            if (graphAsset == null)
            {
                Debug.LogError($"[HeadlessBuilder] Graph Asset not found: {graphAssetPath}");
                return;
            }

            Debug.Log($"[HeadlessBuilder] Loading Graph: {graphAsset.name}");
            Debug.Log($"[HeadlessBuilder] Nodes in graph: {graphAsset.Nodes.Count}, Edges: {graphAsset.Edges.Count}");

            // 2. 内存重建节点 (Instantiate Nodes)
            Dictionary<string, BaseBuildNode> nodeMap = new Dictionary<string, BaseBuildNode>();
            List<BaseBuildNode> allNodes = new List<BaseBuildNode>();

            foreach (var nodeData in graphAsset.Nodes)
            {
                var nodeType = System.Type.GetType(nodeData.NodeType);
                // 容错：尝试在所有 Assembly 查找类型
                if (nodeType == null) nodeType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);

                if (nodeType == null)
                {
                    Debug.LogWarning($"[HeadlessBuilder] Node type not found: {nodeData.NodeType}");
                    continue;
                }

                var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
                node.Initialize();
                node.GUID = nodeData.NodeGUID;
                node.title = nodeData.Title;
                node.LoadFromJSON(nodeData.JsonData);

                nodeMap[node.GUID] = node;
                allNodes.Add(node);
                Debug.Log($"[HeadlessBuilder] Loaded node: {node.title} ({nodeType.Name})");
            }

            if (nodeMap.Count == 0)
            {
                Debug.LogError("[HeadlessBuilder] No nodes found in graph!");
                return;
            }

            // 3. 内存重建连线 (Inject Connections)
            int connectionCount = 0;
            foreach (var edgeData in graphAsset.Edges)
            {
                if (nodeMap.TryGetValue(edgeData.BaseNodeGUID, out var outNode) &&
                    nodeMap.TryGetValue(edgeData.TargetNodeGUID, out var inNode))
                {
                    // 手动注入连接关系，绕过 UI Edge
                    // 注意：BaseNode是输出方，TargetNode是输入方
                    inNode.AddUpstreamConnection(edgeData.TargetPortName, outNode, edgeData.BasePortName);
                    connectionCount++;
                    Debug.Log($"[HeadlessBuilder] Connected: {outNode.title}[{edgeData.BasePortName}] -> {inNode.title}[{edgeData.TargetPortName}]");
                }
                else
                {
                    Debug.LogWarning($"[HeadlessBuilder] Edge references missing node: {edgeData.BaseNodeGUID} -> {edgeData.TargetNodeGUID}");
                }
            }

            Debug.Log($"[HeadlessBuilder] Total connections established: {connectionCount}");

            // 4. 【关键修复】建立Portal节点之间的虚拟连接
            // PortalSender -> PortalReceiver 通过 PortalID 匹配
            LinkPortals(allNodes);

            // 5. 寻找目标节点 - 优先查找 BatchBuildNode
            BaseBuildNode targetNode = null;
            
            // 5.1 首先尝试精确匹配 BatchBuildNode 类型
            targetNode = nodeMap.Values.FirstOrDefault(n => n is BatchBuildNode);
            
            // 5.2 如果没找到 BatchBuildNode，尝试通过名称模糊匹配
            if (targetNode == null)
            {
                foreach (var node in nodeMap.Values)
                {
                    if (node.title.Contains(targetNodeName))
                    {
                        targetNode = node;
                        break;
                    }
                }
            }

            // 5.3 如果仍然没找到，尝试查找 BuildBundleNode
            if (targetNode == null)
            {
                targetNode = nodeMap.Values.FirstOrDefault(n => n is BuildBundleNode);
            }

            if (targetNode == null)
            {
                Debug.LogError($"[HeadlessBuilder] Target node '{targetNodeName}' not found! Available nodes:");
                foreach (var node in nodeMap.Values)
                {
                    Debug.LogError($"  - {node.title} ({node.GetType().Name})");
                }
                return;
            }

            // 检查目标节点的上游连接
            var upstreamConns = targetNode.GetUpstreamConnections();
            Debug.Log($"[HeadlessBuilder] Target node '{targetNode.title}' has {upstreamConns.Count} input ports");
            foreach (var kvp in upstreamConns)
            {
                Debug.Log($"[HeadlessBuilder]   Port '{kvp.Key}': {kvp.Value.Count} upstream nodes");
                foreach (var (upstreamNode, portName) in kvp.Value)
                {
                    Debug.Log($"[HeadlessBuilder]     <- {upstreamNode.title}[{portName}]");
                }
            }

            Debug.Log($"[HeadlessBuilder] Starting Build on Node: {targetNode.title} ({targetNode.GetType().Name})");
            Debug.Log(">>> Batch Build Started <<<");

            // 6. 执行图逻辑 (Headless)
            var context = GraphRunner.RunHeadless(targetNode, true);

            // 7. 输出构建报告
            if (context != null)
            {
                Debug.Log($"[HeadlessBuilder] Assets processed: {context.Assets.Count}");
                if (context.Logs.Length > 0)
                {
                    Debug.Log($"[HeadlessBuilder] Logs:\n{context.Logs}");
                }
                if (context.Reports.Count > 0)
                {
                    Debug.Log("[HeadlessBuilder] Build Reports:");
                    foreach (var report in context.Reports)
                    {
                        string status = report.IsSuccess ? "SUCCESS" : "FAILED";
                        Debug.Log($"  [{status}] {report.NodeTitle} ({report.Category}): {report.Message}");
                        if (!string.IsNullOrEmpty(report.OutputPath))
                        {
                            Debug.Log($"    Output: {report.OutputPath}");
                        }
                        if (report.AssetCount > 0)
                        {
                            Debug.Log($"    Assets: {report.AssetCount}, Size: {report.OutputSizeBytes} bytes");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[HeadlessBuilder] No build reports generated. Check if upstream nodes are properly connected.");
                }
            }
            else
            {
                Debug.LogError("[HeadlessBuilder] Build context is null!");
            }

            Debug.Log(">>> Batch Build Finished <<<");
        }

        /// <summary>
        /// 建立Portal节点之间的虚拟连接
        /// 与GraphRunner中的LinkPortals逻辑一致
        /// </summary>
        private static void LinkPortals(List<BaseBuildNode> allNodes)
        {
            var senderMap = new Dictionary<string, PortalSenderNode>();
            
            // 收集所有PortalSender节点
            foreach (var node in allNodes)
            {
                if (node is PortalSenderNode sender && !string.IsNullOrEmpty(sender.PortalID))
                {
                    senderMap[sender.PortalID] = sender;
                }
            }

            // 为每个PortalReceiver建立到对应Sender的连接
            int portalLinks = 0;
            foreach (var node in allNodes)
            {
                if (node is PortalReceiverNode receiver && !string.IsNullOrEmpty(receiver.PortalID))
                {
                    if (senderMap.TryGetValue(receiver.PortalID, out var sender))
                    {
                        receiver.AddUpstreamConnection("VirtualInput", sender, "Output");
                        portalLinks++;
                        Debug.Log($"[HeadlessBuilder] Portal linked: {sender.title} -> {receiver.title} (ID: {receiver.PortalID})");
                    }
                }
            }

            Debug.Log($"[HeadlessBuilder] Portal connections established: {portalLinks}");
        }

        /// <summary>
        /// 使用默认配置执行打包
        /// </summary>
        public static void BuildWithDefaultConfig()
        {
            string path = EditorPrefs.GetString("BuildGraphPath", "Assets/NewBuildGraph.asset");
            Build(path, "Batch");
        }
    }
}