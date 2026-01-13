using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class RedundancyCheckNode : BaseBuildNode
    {
        public bool ErrorOnRedundancy = false;
        private Toggle _errorToggle;

        public override void Initialize()
        {
            title = "QA: Redundancy Check";
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            AddOutputPort("Pass", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new Color(0.8f, 0.4f, 0.0f); // 橙色

            var container = new VisualElement ();

            _errorToggle = new Toggle("Fail Build on Error") { value = ErrorOnRedundancy };
            _errorToggle.RegisterValueChangedCallback(e => { ErrorOnRedundancy = e.newValue; NotifyChange(); });

            var btn = new Button(OnAnalyzeClick) { text = "Analyze Now", style = { height = 25, marginTop = 5 } };

            container.Add(_errorToggle);
            container.Add(btn);
            container.Add(new Label("Smart analyzer.\nTrims dependency trees and\nhandles SpriteAtlases.") { style = { fontSize = 10, color = Color.gray } });

            mainContainer.Add(container);
        }

        private void OnAnalyzeClick()
        {
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));
            var context = YY.Build.Core.GraphRunner.Run(this, allNodes, false);
            Analyze(context, true);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            bool hasIssues = Analyze(context, false);
            if (hasIssues && ErrorOnRedundancy && context.IsBuildMode)
            {
                throw new System.Exception("[RedundancyCheck] Build stopped due to redundant assets! Check logs.");
            }
            return base.Execute(context);
        }

        // --- 辅助：统一路径格式 ---
        private string Normalize(string path)
        {
            return path.Replace("\\", "/");
        }

        private bool Analyze(BuildContext context, bool openFile)
        {
            context.Logs.AppendLine("[RedundancyCheck] Starting Smart Analysis...");

            // --- 阶段 1: 建立显式映射 (Explicit Map) ---
            // 记录 [资源路径 -> 所属 Bundle]
            Dictionary<string, string> explicitMap = new Dictionary<string, string>();
            Dictionary<string, List<string>> bundleContents = new Dictionary<string, List<string>>();

            foreach (var asset in context.Assets)
            {
                if (string.IsNullOrEmpty(asset.BundleName)) continue;

                // 【修复】路径标准化
                string path = Normalize(asset.AssetPath);

                explicitMap[path] = asset.BundleName;

                if (!bundleContents.ContainsKey(asset.BundleName)) bundleContents[asset.BundleName] = new List<string>();
                bundleContents[asset.BundleName].Add(path);
            }

            // --- 阶段 2: 智能图集扩展 (Fix Atlas Redirection) ---
            // 找到所有被显式打包的 .spriteatlas 文件
            var atlasPaths = explicitMap.Keys.Where(p => p.EndsWith(".spriteatlasv2", System.StringComparison.OrdinalIgnoreCase)).ToList();
            int packedSpriteCount = 0;

            foreach (var atlasPath in atlasPaths)
            {
                string atlasBundle = explicitMap[atlasPath];

                // 获取图集的所有依赖 (Recursive=true)，这会包含它里面的所有 Sprite 和 Texture
                string[] packedSprites = AssetDatabase.GetDependencies(atlasPath, true);

                foreach (var rawPath in packedSprites)
                {
                    // 【修复】路径标准化
                    string spritePath = Normalize(rawPath);

                    if (spritePath != atlasPath && !spritePath.EndsWith(".cs") && !spritePath.EndsWith(".dll"))
                    {
                        // 如果散图还没有被分配 Bundle，我们强制把它归属到图集所在的 Bundle
                        if (!explicitMap.ContainsKey(spritePath))
                        {
                            explicitMap[spritePath] = atlasBundle;
                            packedSpriteCount++;
                        }
                    }
                }
            }
            context.Logs.AppendLine($"  [Atlas] Mapped {packedSpriteCount} packed sprites to their atlas bundles.");

            // --- 阶段 3: 智能依赖树 BFS 遍历 ---
            // 记录 [隐式资源 -> 被哪些 Bundle 引用]
            Dictionary<string, HashSet<string>> implicitUsage = new Dictionary<string, HashSet<string>>();

            foreach (var kvp in bundleContents)
            {
                string currentBundle = kvp.Key;
                List<string> roots = kvp.Value;

                // 使用队列进行 BFS
                Queue<string> queue = new Queue<string>(roots);
                HashSet<string> visited = new HashSet<string>();

                while (queue.Count > 0)
                {
                    string currentAsset = queue.Dequeue();
                    if (visited.Contains(currentAsset)) continue;
                    visited.Add(currentAsset);

                    // 仅获取直接依赖
                    string[] directDeps = AssetDatabase.GetDependencies(new[] { currentAsset }, false);

                    foreach (var rawDep in directDeps)
                    {
                        // 【修复】路径标准化
                        string dep = Normalize(rawDep);

                        if (dep == currentAsset) continue;
                        if (dep.EndsWith(".cs") || dep.EndsWith(".dll") || System.IO.Directory.Exists(dep)) continue;
                        if (!dep.StartsWith("Assets/") && !dep.StartsWith("Packages/")) continue;

                        // 【核心判断】
                        if (explicitMap.TryGetValue(dep, out string ownerBundle))
                        {
                            // A. 依赖项属于【其他 Bundle】 -> 剪枝，停止深入
                            if (ownerBundle != currentBundle)
                            {
                                // 这是一个合法的跨包引用，SBP 会处理，不是冗余，也不需要继续遍历
                                continue;
                            }
                            // B. 依赖项属于【自己这个 Bundle】 -> 继续深入遍历
                            else
                            {
                                queue.Enqueue(dep);
                            }
                        }
                        else
                        {
                            // C. 依赖项没有主人 -> 它是隐式资源
                            // 记录“罪证”
                            if (!implicitUsage.ContainsKey(dep)) implicitUsage[dep] = new HashSet<string>();
                            implicitUsage[dep].Add(currentBundle);

                            // 继续向下探索，看看它后面还拖着什么
                            queue.Enqueue(dep);
                        }
                    }
                }
            }

            // --- 阶段 4: 统计与输出 ---
            var redundantAssets = implicitUsage.Where(x => x.Value.Count > 1).ToList();

            if (redundantAssets.Count == 0)
            {
                context.Logs.AppendLine($"  Result: PERFECT! No redundancy found in {bundleContents.Count} bundles.");
                Debug.Log("[RedundancyCheck] Passed. No redundancy found.");
                return false;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== REDUNDANCY REPORT ===");
                sb.AppendLine($"Found {redundantAssets.Count} duplicated assets.");
                sb.AppendLine("These assets are NOT assigned to any bundle (Implicit), and are referenced by multiple bundles.");
                sb.AppendLine("--------------------------------------------------");

                // 按大小排序
                var sortedList = redundantAssets.OrderByDescending(x =>
                {
                    var info = new System.IO.FileInfo(x.Key);
                    return info.Exists ? info.Length : 0;
                }).ToList();

                foreach (var item in sortedList)
                {
                    long sizeKb = 0;
                    if (System.IO.File.Exists(item.Key)) sizeKb = new System.IO.FileInfo(item.Key).Length / 1024;

                    sb.AppendLine($"[ {sizeKb} KB ] {item.Key}");
                    sb.AppendLine($"  Duplicated in ({item.Value.Count}):");
                    foreach (var b in item.Value) sb.AppendLine($"    - {b}");
                    sb.AppendLine();
                }

                context.Logs.Append(sb.ToString());
                Debug.LogError($"[RedundancyCheck] Found {redundantAssets.Count} redundant assets!");

                string reportPath = "RedundancyReport.txt";
                System.IO.File.WriteAllText(reportPath, sb.ToString());
                if (openFile) EditorUtility.OpenWithDefaultApp(reportPath);

                return true;
            }
        }

        [System.Serializable] class NodeData { public bool error; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { error = ErrorOnRedundancy });
        public override void LoadFromJSON(string json)
        {
            var d = JsonUtility.FromJson<NodeData>(json);
            if (d != null) { ErrorOnRedundancy = d.error; if (_errorToggle != null) _errorToggle.value = d.error; }
        }
    }
}