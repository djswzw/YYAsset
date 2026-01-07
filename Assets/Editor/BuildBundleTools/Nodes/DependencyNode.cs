using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.U2D;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    public class DependencyNode : BaseBuildNode
    {
        // --- 配置数据 ---
        public bool Recursive = true;
        public bool SmartAtlas = true;

        private Toggle _recursiveToggle;
        private Toggle _smartAtlasToggle;

        public override void Initialize()
        {
            base.Initialize();
            title = "Process: Dependency";

            // 端口定义
            AddInputPort("Input", Port.Capacity.Multi);
            AddOutputPort("Combined", Port.Capacity.Multi);   // 所有 (Roots + Deps)
            AddOutputPort("Deps Only", Port.Capacity.Multi);  // 仅依赖 (Deps - Roots)

            // UI 定义
            var container = new VisualElement { style = { paddingTop = 5, paddingBottom = 5 } };

            _recursiveToggle = new Toggle("Recursive Check") { value = Recursive };
            _recursiveToggle.RegisterValueChangedCallback(e => { Recursive = e.newValue; NotifyChange(); });

            _smartAtlasToggle = new Toggle("Smart Sprite Atlas") { value = SmartAtlas };
            _smartAtlasToggle.RegisterValueChangedCallback(e => { SmartAtlas = e.newValue; NotifyChange(); });
            _smartAtlasToggle.tooltip = "If enabled, replaces raw Sprites/Textures with their .spriteatlas file.";

            container.Add(_recursiveToggle);
            container.Add(_smartAtlasToggle);

            // 提示文本
            var tip = new Label("Deps Only will exclude\noriginal input assets.");
            tip.style.color = Color.gray;
            tip.style.fontSize = 10;
            container.Add(tip);

            mainContainer.Add(container);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[DependencyNode] Analyzing dependencies (Recursive: {Recursive}, SmartAtlas: {SmartAtlas})...");

            var combinedContext = new BuildContext();
            var depsOnlyContext = new BuildContext();

            // 继承上游日志
            combinedContext.Logs.Append(context.Logs);
            depsOnlyContext.Logs.Append(context.Logs);

            // 1. 准备集合
            // knownPaths: 用于 Combined 去重，防止重复添加
            HashSet<string> knownPaths = new HashSet<string>();

            // inputPathSet: 【关键】记录原始输入的资源路径，用于 DepsOnly 剔除
            HashSet<string> inputPathSet = new HashSet<string>();

            foreach (var asset in context.Assets)
            {
                knownPaths.Add(asset.AssetPath);
                inputPathSet.Add(asset.AssetPath);

                // 根资源直接进入 Combined，但暂时不进 DepsOnly
                combinedContext.Assets.Add(asset);
            }

            // 2. 构建图集映射 (Texture Path -> Atlas Path)
            Dictionary<string, string> texToAtlasMap = new Dictionary<string, string>();
            if (SmartAtlas)
            {
                BuildAtlasMap(ref texToAtlasMap);
                context.Logs.AppendLine($"  [SmartAtlas] Indexed {texToAtlasMap.Count} packed textures.");
            }

            // 3. 查找依赖
            string[] rootPaths = context.Assets.Select(a => a.AssetPath).ToArray();
            string[] dependencyPaths = AssetDatabase.GetDependencies(rootPaths, Recursive);

            int addedCount = 0;
            int atlasSwappedCount = 0;

            foreach (var path in dependencyPaths)
            {
                // A. 基础过滤
                if (path.EndsWith(".cs") || path.EndsWith(".dll")) continue; // 排除脚本
                if (!path.StartsWith("Assets/")) continue; // 排除内置资源
                if (System.IO.Directory.Exists(path)) continue; // 排除文件夹

                // 路径标准化
                string finalPath = path.Replace("\\", "/");

                // B. 图集智能替换
                if (SmartAtlas && texToAtlasMap.TryGetValue(finalPath, out string atlasPath))
                {
                    // 如果图片属于图集，且图集本身不在 knownPaths 里，则替换为图集路径
                    // 逻辑：如果输入已经是图集了，这里就不会替换（因为 map key 是图片）
                    // 但如果 map key 是图片，说明它是散图依赖，应该换成图集
                    finalPath = atlasPath;
                    atlasSwappedCount++;
                }

                // C. 【关键判断】是否为根资源？
                // 注意：如果 SmartAtlas 把图片换成了图集，finalPath 变了，
                // 此时我们要检查这个新的 finalPath (图集) 是否在输入列表里。
                bool isRootAsset = inputPathSet.Contains(finalPath);

                // D. 分发逻辑
                // 如果 Combined 里还没有这个资源 (去重)
                if (!knownPaths.Contains(finalPath))
                {
                    knownPaths.Add(finalPath);
                    var newAsset = new AssetBuildInfo(finalPath);

                    // 1. 总是加入 Combined
                    combinedContext.Assets.Add(newAsset);

                    // 2. 只有非根资源才加入 Deps Only
                    if (!isRootAsset)
                    {
                        depsOnlyContext.Assets.Add(newAsset);
                    }

                    addedCount++;
                }
                else
                {
                    // 即使 knownPaths 已经有了，也要检查逻辑一致性。
                    // 比如：如果该资源是 Root，它已经在 Step 1 加过 Combined 了。
                    // 如果该资源是之前的依赖加过的，也处理过了。
                    // 所以这里不用做额外操作。
                }
            }

            context.Logs.AppendLine($"  Found {addedCount} new unique dependencies.");
            if (SmartAtlas) context.Logs.AppendLine($"  Swapped {atlasSwappedCount} textures to Atlases.");

            return new Dictionary<string, BuildContext>
            {
                { "Combined", combinedContext },
                { "Deps Only", depsOnlyContext }
            };
        }

        // 辅助：建立图片到图集的反向索引
        private void BuildAtlasMap(ref Dictionary<string, string> map)
        {
            string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            foreach (var guid in guids)
            {
                string atlasPath = AssetDatabase.GUIDToAssetPath(guid);
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (atlas == null) continue;

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
                            foreach (var f in files)
                            {
                                if (f.EndsWith(".meta")) continue;
                                string p = f.Replace("\\", "/");
                                if (!map.ContainsKey(p)) map[p] = atlasPath;
                            }
                        }
                        else
                        {
                            if (!map.ContainsKey(objPath)) map[objPath] = atlasPath;
                        }
                    }
                }
            }
        }

        // --- 序列化 ---
        [System.Serializable] class NodeData { public bool recursive; public bool smartAtlas; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { recursive = Recursive, smartAtlas = SmartAtlas });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                Recursive = data.recursive;
                SmartAtlas = data.smartAtlas;
                if (_recursiveToggle != null) _recursiveToggle.value = Recursive;
                if (_smartAtlasToggle != null) _smartAtlasToggle.value = SmartAtlas;
            }
        }
    }
}