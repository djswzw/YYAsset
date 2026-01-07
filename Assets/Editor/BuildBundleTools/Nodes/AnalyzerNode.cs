using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class AnalyzerNode : BaseBuildNode
    {
        public bool Recursive = true;
        private Toggle _recursiveToggle;

        public override void Initialize()
        {
            base.Initialize();
            title = "Analysis: Auto Common";

            // 允许多路输入，每一路代表一个独立的“资源组”
            AddInputPort("Inputs", Port.Capacity.Multi);

            // 输出：被多个组共用的资源
            var sharedPort = AddOutputPort("Shared Assets", Port.Capacity.Multi);
            sharedPort.portColor = Color.yellow; // 醒目颜色

            // 输出：仅被单个组引用的资源 (通常用于调试或全自动合并打包)
            AddOutputPort("Unique Assets", Port.Capacity.Multi);

            var container = new VisualElement { style = { paddingTop = 5, paddingBottom = 5 } };
            _recursiveToggle = new Toggle("Recursive Check") { value = Recursive };
            _recursiveToggle.RegisterValueChangedCallback(e => { Recursive = e.newValue; NotifyChange(); });

            var label = new Label("Identifies assets referenced\nby 2+ input groups.");
            label.style.color = Color.gray;
            label.style.fontSize = 10;

            container.Add(_recursiveToggle);
            container.Add(label);
            mainContainer.Add(container);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext ignoredContext)
        {
            var sharedCtx = new BuildContext();
            var uniqueCtx = new BuildContext();
            sharedCtx.Logs.AppendLine("[Analyzer] Analyzing Reference Counts...");

            // 1. 获取所有连接到 "Inputs" 的上游节点
            // 这里的关键是：每一个上游连线，代表一个“逻辑组”
            var inputGroups = GetUpstreamGroups("Inputs");

            if (inputGroups.Count < 2)
            {
                sharedCtx.Logs.AppendLine("  Warning: Need at least 2 input groups to find shared assets.");
                // 如果只有一个组，那所有东西都是 Unique
                foreach (var group in inputGroups) uniqueCtx.Assets.AddRange(group);
                return new Dictionary<string, BuildContext> { { "Shared Assets", sharedCtx }, { "Unique Assets", uniqueCtx } };
            }

            // 2. 建立 资源路径 -> 引用它的组ID列表 映射
            // Key: AssetPath, Value: Set<GroupID>
            Dictionary<string, HashSet<int>> refMap = new Dictionary<string, HashSet<int>>();

            // 缓存所有依赖，避免重复计算
            int groupIndex = 0;
            foreach (var groupAssets in inputGroups)
            {
                // 计算该组的所有依赖
                string[] roots = groupAssets.Select(a => a.AssetPath).ToArray();
                string[] dependencies = AssetDatabase.GetDependencies(roots, Recursive);

                foreach (var path in dependencies)
                {
                    // 过滤脚本等
                    if (path.EndsWith(".cs") || path.EndsWith(".dll") || System.IO.Directory.Exists(path)) continue;

                    string finalPath = path.Replace("\\", "/");

                    if (!refMap.ContainsKey(finalPath))
                        refMap[finalPath] = new HashSet<int>();

                    refMap[finalPath].Add(groupIndex);
                }
                groupIndex++;
            }

            // 3. 分析引用计数
            int sharedCount = 0;
            int uniqueCount = 0;

            foreach (var kvp in refMap)
            {
                string path = kvp.Key;
                int refCount = kvp.Value.Count;

                var assetInfo = new AssetBuildInfo(path);

                if (refCount > 1)
                {
                    // 被 2 个以上组引用 -> Shared
                    sharedCtx.Assets.Add(assetInfo);
                    sharedCount++;
                }
                else
                {
                    // 只被 1 个组引用 -> Unique
                    uniqueCtx.Assets.Add(assetInfo);
                    uniqueCount++;
                }
            }

            sharedCtx.Logs.AppendLine($"  Analyzed {inputGroups.Count} groups.");
            sharedCtx.Logs.AppendLine($"  Found {sharedCount} shared assets (Auto Common).");
            sharedCtx.Logs.AppendLine($"  Found {uniqueCount} unique assets.");

            return new Dictionary<string, BuildContext> {
                { "Shared Assets", sharedCtx },
                { "Unique Assets", uniqueCtx }
            };
        }

        // --- 辅助方法：获取每一路输入的数据 ---
        // 我们需要修改 BaseBuildNode 来支持这个，或者在这里通过反射/Hardcode 获取
        // 为了架构整洁，建议在 BaseBuildNode 增加一个 GetUpstreamDataSources 方法
        private List<List<AssetBuildInfo>> GetUpstreamGroups(string portName)
        {
            var groups = new List<List<AssetBuildInfo>>();

            // 获取端口
            var port = inputContainer.Q<Port>(portName);
            if (port == null || !port.connected) return groups;

            foreach (var edge in port.connections)
            {
                var upstreamNode = edge.output.node as BaseBuildNode;
                var upstreamPort = edge.output.portName;

                if (YY.Build.Core.GraphRunner.DataMap != null &&
                    YY.Build.Core.GraphRunner.DataMap.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                    nodeOutputs.TryGetValue(upstreamPort, out var data))
                {
                    // 每一根线的数据，作为一个独立的 List 加入
                    groups.Add(new List<AssetBuildInfo>(data.Assets)); // Clone list
                }
            }
            return groups;
        }

        // 序列化
        [System.Serializable] class NodeData { public bool recursive; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { recursive = Recursive });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { Recursive = data.recursive; if (_recursiveToggle != null) _recursiveToggle.value = Recursive; }
        }
    }
}