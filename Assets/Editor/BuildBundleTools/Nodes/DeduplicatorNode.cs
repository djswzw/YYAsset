using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class DeduplicatorNode : BaseBuildNode
    {
        public bool Recursive = true;
        private Toggle _recursiveToggle;

        public override void Initialize()
        {
            title = "Process: Deduplicator";

            // --- 输入 ---
            AddInputPort("Source", Port.Capacity.Multi);
            var reservedPort = AddInputPort("Reserved (Exclude)", Port.Capacity.Multi);
            reservedPort.portColor = new Color(1f, 0.4f, 0.4f); // 红色端口提示排除

            // --- 输出 ---
            // 1. 包含源文件 + 唯一依赖 (适合打整包)
            AddOutputPort("Combined (Unique)", Port.Capacity.Multi);

            // 2. 仅包含唯一依赖，剔除源文件 (适合分离打包) -> 【这就是你现在需要的】
            AddOutputPort("Deps Only (Unique)", Port.Capacity.Multi);

            // --- UI ---
            var container = new VisualElement { style = { paddingTop = 5, paddingBottom = 5 } };
            _recursiveToggle = new Toggle("Recursive Check") { value = Recursive };
            _recursiveToggle.RegisterValueChangedCallback(e => { Recursive = e.newValue; NotifyChange(); });

            var label = new Label("Calculates Source dependencies,\nremoves 'Reserved' items,\nand splits Roots vs Deps.");
            label.style.color = Color.gray;
            label.style.fontSize = 10;

            container.Add(_recursiveToggle);
            container.Add(label);
            mainContainer.Add(container);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext ignoredContext)
        {
            // 1. 拉取数据
            var sourceCtx = GetInputContext("Source");
            var reservedCtx = GetInputContext("Reserved (Exclude)");

            var combinedCtx = new BuildContext();
            var depsOnlyCtx = new BuildContext();

            // 日志合并
            combinedCtx.Logs.Append(sourceCtx.Logs);
            depsOnlyCtx.Logs.Append(sourceCtx.Logs);
            combinedCtx.Logs.AppendLine("[Deduplicator] Processing...");

            // 2. 记录源文件集合 (Root Set) -> 用于 DepsOnly 剔除自身
            HashSet<string> rootSet = new HashSet<string>();
            foreach (var asset in sourceCtx.Assets)
            {
                rootSet.Add(asset.AssetPath);
            }

            // 3. 构建黑名单 (Reserved Set)
            HashSet<string> blackList = new HashSet<string>();
            foreach (var asset in reservedCtx.Assets)
            {
                blackList.Add(asset.AssetPath);

                // 智能扩展：如果是图集，把里面的子图也拉黑
                if (asset.AssetPath.EndsWith(".spriteatlasv2"))
                {
                    var packedSprites = GetSpritesInAtlas(asset.AssetPath);
                    foreach (var s in packedSprites) blackList.Add(s);
                }
            }
            combinedCtx.Logs.AppendLine($"  Reserved List size: {blackList.Count}");

            // 4. 计算依赖
            string[] rootPaths = rootSet.ToArray();
            string[] dependencies = AssetDatabase.GetDependencies(rootPaths, Recursive);

            // 5. 过滤逻辑
            int totalAdded = 0;
            int reservedSkipped = 0;

            // 内部去重，防止输出里有重复项
            HashSet<string> processedOutputs = new HashSet<string>();

            foreach (var path in dependencies)
            {
                // A. 基础过滤
                if (path.EndsWith(".cs") || path.EndsWith(".dll") || System.IO.Directory.Exists(path)) continue;

                string finalPath = path.Replace("\\", "/");

                // B. 防止重复处理
                if (processedOutputs.Contains(finalPath)) continue;
                processedOutputs.Add(finalPath);

                // C. 【第一关】黑名单检查
                if (blackList.Contains(finalPath))
                {
                    reservedSkipped++;
                    continue; // 在保留区里，跳过
                }

                // D. 【第二关】分发逻辑
                var newAsset = new AssetBuildInfo(finalPath);

                // D1. Combined: 只要没被拉黑，都放进来 (Source + New Deps)
                combinedCtx.Assets.Add(newAsset);

                // D2. Deps Only: 既没被拉黑，也不是 Source 本身 (New Deps)
                if (!rootSet.Contains(finalPath))
                {
                    depsOnlyCtx.Assets.Add(newAsset);
                }

                totalAdded++;
            }

            combinedCtx.Logs.AppendLine($"  Total Unique Assets: {totalAdded}. Skipped {reservedSkipped} reserved.");

            return new Dictionary<string, BuildContext> {
                { "Combined (Unique)", combinedCtx },
                { "Deps Only (Unique)", depsOnlyCtx }
            };
        }
        private List<string> GetSpritesInAtlas(string atlasPath)
        {
            var results = new List<string>();
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null) return results;

            SerializedObject so = new SerializedObject(atlas);
            SerializedProperty packables = so.FindProperty("m_EditorData.packables");
            if (packables != null)
            {
                for (int i = 0; i < packables.arraySize; i++)
                {
                    Object obj = packables.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (obj == null) continue;
                    string objPath = AssetDatabase.GetAssetPath(obj);
                    if (System.IO.Directory.Exists(objPath))
                    {
                        string[] files = System.IO.Directory.GetFiles(objPath, "*.*", System.IO.SearchOption.AllDirectories);
                        foreach (var f in files) if (!f.EndsWith(".meta")) results.Add(f.Replace("\\", "/"));
                    }
                    else results.Add(objPath);
                }
            }
            return results;
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