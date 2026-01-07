using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class SubGraphNode : BaseBuildNode
    {
        public BuildGraphAsset SubGraphAsset;
        private ObjectField _assetField;

        // 防止无限递归
        private static HashSet<BuildGraphAsset> _recursionStack = new HashSet<BuildGraphAsset>();

        public override void Initialize()
        {
            base.Initialize();
            title = "Container: Sub-Graph";

            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            AddOutputPort("Output", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            _assetField = new ObjectField("Graph Asset:")
            {
                objectType = typeof(BuildGraphAsset),
                value = SubGraphAsset,
                allowSceneObjects = false
            };

            _assetField.RegisterValueChangedCallback(e =>
            {
                SubGraphAsset = e.newValue as BuildGraphAsset;
                NotifyChange();
            });

            mainContainer.Add(_assetField);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            if (SubGraphAsset == null)
            {
                context.Logs.AppendLine($"[SubGraph] Error: No graph asset assigned.");
                return base.Execute(context);
            }

            if (_recursionStack.Contains(SubGraphAsset))
            {
                context.Logs.AppendLine($"[SubGraph] Error: Cyclic recursion detected! Skipping {SubGraphAsset.name}");
                return base.Execute(context);
            }
            _recursionStack.Add(SubGraphAsset);

            try
            {
                context.Logs.AppendLine($"[SubGraph] Entering '{SubGraphAsset.name}'...");

                // 1. 内存化加载子图
                var nodes = LoadNodesFromAsset(SubGraphAsset);

                // 2. 寻找入口和出口
                var inputNode = nodes.FirstOrDefault(n => n is SubGraphInputNode) as SubGraphInputNode;
                var outputNode = nodes.FirstOrDefault(n => n is SubGraphOutputNode) as SubGraphOutputNode;

                // 【核心修复】SubGraphInputNode 现在是可选的
                if (inputNode != null)
                {
                    // 2.1 如果子图有入口，注入父图数据
                    // 复制一份 context，避免污染父图上下文（尤其是 Logs）
                    var subContext = new BuildContext();
                    subContext.Assets.AddRange(context.Assets);
                    subContext.Logs.AppendLine("(Inside SubGraph)");

                    inputNode.InjectedContext = subContext;
                }
                else
                {
                    // 2.2 如果子图没有入口，说明它是“生产型”子图
                    if (context.Assets.Count > 0)
                    {
                        context.Logs.AppendLine("  [Notice] Sub-Graph has no Input Node. Parent input assets are ignored.");
                    }
                }

                // 3. 执行子图
                if (outputNode != null)
                {
                    // 从 OutputNode 开始反向运行 GraphRunner
                    // 它会自动找到内部的 DirectoryNode 并开始执行
                    var resultCtx = GraphRunner.RunHeadless(outputNode);

                    context.Logs.AppendLine($"[SubGraph] Exiting '{SubGraphAsset.name}'. Generated {resultCtx.Assets.Count} assets.");

                    // 返回子图的结果
                    var finalOutput = new BuildContext();
                    finalOutput.Assets = resultCtx.Assets;
                    finalOutput.Logs = context.Logs; // 保持父图日志连贯性

                    return new Dictionary<string, BuildContext> { { "Output", finalOutput } };
                }
                else
                {
                    context.Logs.AppendLine("  Warning: Sub-Graph has no 'SubGraph Output' node. Returning empty.");
                    return base.Execute(context);
                }
            }
            finally
            {
                _recursionStack.Remove(SubGraphAsset);
            }
        }

        // --- 辅助：内存化加载图 (保持不变) ---
        private List<BaseBuildNode> LoadNodesFromAsset(BuildGraphAsset asset)
        {
            var nodeMap = new Dictionary<string, BaseBuildNode>();
            var nodeList = new List<BaseBuildNode>();

            foreach (var nodeData in asset.Nodes)
            {
                var nodeType = System.Type.GetType(nodeData.NodeType);
                if (nodeType == null) nodeType = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == nodeData.NodeType);
                if (nodeType == null) continue;

                var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
                node.Initialize();
                node.GUID = nodeData.NodeGUID;
                node.LoadFromJSON(nodeData.JsonData);
                nodeMap[node.GUID] = node;
                nodeList.Add(node);
            }

            foreach (var edgeData in asset.Edges)
            {
                if (nodeMap.TryGetValue(edgeData.BaseNodeGUID, out var outNode) &&
                    nodeMap.TryGetValue(edgeData.TargetNodeGUID, out var inNode))
                {
                    inNode.AddUpstreamConnection(edgeData.TargetPortName, outNode, edgeData.BasePortName);
                }
            }

            // 手动处理内部 Portal 连接
            var senderMap = new Dictionary<string, PortalSenderNode>();
            foreach (var n in nodeList) if (n is PortalSenderNode s) senderMap[s.PortalID] = s;
            foreach (var n in nodeList) if (n is PortalReceiverNode r && senderMap.TryGetValue(r.PortalID, out var s))
                    r.AddUpstreamConnection("VirtualInput", s, "Output");

            return nodeList;
        }

        [System.Serializable] class NodeData { public string assetGuid; }
        public override string SaveToJSON()
        {
            string guid = "";
            if (SubGraphAsset != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(SubGraphAsset, out guid, out long _);
            return JsonUtility.ToJson(new NodeData { assetGuid = guid });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null && !string.IsNullOrEmpty(data.assetGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(data.assetGuid);
                SubGraphAsset = AssetDatabase.LoadAssetAtPath<BuildGraphAsset>(path);
                if (_assetField != null) _assetField.value = SubGraphAsset;
            }
        }
    }
}