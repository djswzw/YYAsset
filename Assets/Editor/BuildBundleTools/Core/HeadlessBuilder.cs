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
        /// <param name="targetNodeName">要执行的打包节点名称 (支持模糊匹配)</param>
        public static void Build(string graphAssetPath, string targetNodeName)
        {
            // 1. 加载 Asset
            var graphAsset = AssetDatabase.LoadAssetAtPath<BuildGraphAsset>(graphAssetPath);
            if (graphAsset == null)
            {
                Debug.LogError($"[HeadlessBuilder] Graph Asset not found: {graphAssetPath}");
                return;
            }

            Debug.Log($"[HeadlessBuilder] Loading Graph: {graphAsset.name}");

            // 2. 内存重建节点 (Instantiate Nodes)
            Dictionary<string, BaseBuildNode> nodeMap = new Dictionary<string, BaseBuildNode>();

            foreach (var nodeData in graphAsset.Nodes)
            {
                var nodeType = System.Type.GetType(nodeData.NodeType);
                // 容错：尝试在所有 Assembly 查找类型
                if (nodeType == null) nodeType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);

                if (nodeType == null) continue;

                var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
                // 注意：Initialize 可能会创建 UI 元素，在 BatchMode 下部分 UI API 可能受限
                // 但 VisualElement 本身通常是安全的。如果有问题，需要把数据初始化和 UI 初始化分离。
                // 暂时假设它是安全的。
                node.Initialize();
                node.GUID = nodeData.NodeGUID;
                node.title = nodeData.Title;
                node.LoadFromJSON(nodeData.JsonData); // 恢复数据 (路径、规则等)

                nodeMap[node.GUID] = node;
            }

            // 3. 内存重建连线 (Inject Connections)
            foreach (var edgeData in graphAsset.Edges)
            {
                if (nodeMap.TryGetValue(edgeData.BaseNodeGUID, out var outNode) &&
                    nodeMap.TryGetValue(edgeData.TargetNodeGUID, out var inNode))
                {
                    // 手动注入连接关系，绕过 UI Edge
                    inNode.AddUpstreamConnection(edgeData.TargetPortName, outNode, edgeData.BasePortName);
                }
            }

            // 4. 寻找目标节点
            BaseBuildNode targetNode = null;
            foreach (var node in nodeMap.Values)
            {
                if (node is BuildBundleNode && node.title.Contains(targetNodeName))
                {
                    targetNode = node;
                    break;
                }
            }

            if (targetNode == null)
            {
                Debug.LogError($"[HeadlessBuilder] Target BuildBundleNode '{targetNodeName}' not found!");
                return;
            }

            Debug.Log($"[HeadlessBuilder] Starting Build on Node: {targetNode.title}");

            // 5. 执行图逻辑 (Headless)
            // 因为我们已经手动 AddUpstreamConnection 了，GraphRunner.RunHeadless 可以直接跑
            var context = GraphRunner.RunHeadless(targetNode, true);

            Debug.Log("[HeadlessBuilder] Build Complete.");
        }

        public static void TestHeadless()
        {
            // 假设你的 Graph 文件在这里，请根据实际情况修改
            string path = "Assets/NewBuildGraph.asset";

            // 假设你的打包节点叫 "Export: Build AssetBundles" (或者你重命名后的名字)
            string nodeName = "Build";

            Build(path, nodeName);
        }
    }
}