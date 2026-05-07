using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;

namespace YY.Build.Graph.Nodes
{
    public class BuildBundleNode : BaseBuildNode
    {
        // --- 数据 ---
        public string OutputPath = "StreamingRes/Bundles";
        public BuildTarget TargetPlatform = BuildTarget.StandaloneWindows64;
        public BuildAssetBundleOptions BuildOptions = BuildAssetBundleOptions.None;
        public string ManifestName = "sys_manifest";

        private TextField _pathField;
        private TextField _manifestField;
        private EnumField _targetField;
        private EnumField _compField;
        private Dictionary<BuildAssetBundleOptions, Toggle> _optionToggles = new Dictionary<BuildAssetBundleOptions, Toggle>();

        private enum CompressionType { LZMA_Default, LZ4_ChunkBased, Uncompressed }
        private CompressionType _compressionType = CompressionType.LZ4_ChunkBased;

        public override void Initialize()
        {
            base.Initialize();
            title = "Export: Build AssetBundles";

            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            AddOutputPort("Pass", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            //初始化数据
            ParseOptionsToUI();

            //构建 UI
            DrawUI();
        }

        private void DrawUI()
        {
            var root = new VisualElement();
            root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            root.style.paddingTop = 5; root.style.paddingBottom = 5;
            root.style.paddingLeft = 5; root.style.paddingRight = 5;
            root.style.width = 260;

            // --- A. 输出路径 ---
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _pathField = new TextField("Output:") { value = OutputPath, style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(e => { OutputPath = e.newValue; NotifyChange(); });

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Directory", OutputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath)) path = "Assets" + path.Substring(Application.dataPath.Length);
                    else if (path.Contains(Application.dataPath + "/../")) path = path.Replace(Application.dataPath + "/../", "");
                    OutputPath = path;
                    _pathField.value = path;
                    NotifyChange();
                }
            })
            { text = "..." };
            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);

            _manifestField = new TextField("Manifest Name:") { value = ManifestName };
            _manifestField.RegisterValueChangedCallback(e => { ManifestName = e.newValue; NotifyChange(); });
            root.Add(_manifestField);

            // --- B. 平台 ---
            if (TargetPlatform == 0) TargetPlatform = EditorUserBuildSettings.activeBuildTarget;
            _targetField = new EnumField("Target:", TargetPlatform);
            _targetField.RegisterValueChangedCallback(e => { TargetPlatform = (BuildTarget)e.newValue; NotifyChange(); });

            // --- C. 压缩 ---
            _compField = new EnumField("Compression:", _compressionType);
            _compField.RegisterValueChangedCallback(e =>
            {
                _compressionType = (CompressionType)e.newValue;
                UpdateOptionsFromUI();
                NotifyChange();
            });

            // --- D. 常用开关 ---
            var forceToggle = CreateFlagToggle("Force Rebuild", BuildAssetBundleOptions.ForceRebuildAssetBundle);
            var hashToggle = CreateFlagToggle("Append Hash", BuildAssetBundleOptions.AppendHashToAssetBundleName);

            // --- E. 高级选项折叠 ---
            var foldout = new Foldout { text = "Advanced Options", value = false };
            foldout.style.marginTop = 5;
            foldout.Add(CreateFlagToggle("Strict Mode", BuildAssetBundleOptions.StrictMode));
            foldout.Add(CreateFlagToggle("Dry Run Build", BuildAssetBundleOptions.DryRunBuild));
            foldout.Add(CreateFlagToggle("Disable Write TypeTree", BuildAssetBundleOptions.DisableWriteTypeTree));
            foldout.Add(CreateFlagToggle("Ignore TypeTree Changes", BuildAssetBundleOptions.IgnoreTypeTreeChanges));

            // --- F. Build 按钮 ---
            var buildBtn = new Button(OnBuildClick)
            {
                text = "BUILD Bundles",
                style = { height = 30, marginTop = 10, backgroundColor = new Color(0.2f, 0.6f, 0.2f), unityFontStyleAndWeight = FontStyle.Bold }
            };

            root.Add(pathRow);
            root.Add(_targetField);
            root.Add(_compField);
            root.Add(forceToggle);
            root.Add(hashToggle);
            root.Add(foldout);
            root.Add(buildBtn);

            mainContainer.Add(root);
        }

        private Toggle CreateFlagToggle(string label, BuildAssetBundleOptions flag)
        {
            bool hasFlag = (BuildOptions & flag) != 0;
            var toggle = new Toggle(label) { value = hasFlag };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) BuildOptions |= flag;
                else BuildOptions &= ~flag;
                NotifyChange();
            });

            _optionToggles[flag] = toggle;

            return toggle;
        }

        private void OnBuildClick()
        {
            UpdateOptionsFromUI();
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = graphView.nodes.ToList().Cast<BaseBuildNode>().ToList();

            var context = GraphRunner.Run(this, allNodes,true);
        }

        // --- 逻辑辅助 ---
        private void ParseOptionsToUI()
        {
            if ((BuildOptions & BuildAssetBundleOptions.ChunkBasedCompression) != 0) _compressionType = CompressionType.LZ4_ChunkBased;
            else if ((BuildOptions & BuildAssetBundleOptions.UncompressedAssetBundle) != 0) _compressionType = CompressionType.Uncompressed;
            else _compressionType = CompressionType.LZMA_Default;
        }

        private void UpdateOptionsFromUI()
        {
            BuildOptions &= ~BuildAssetBundleOptions.ChunkBasedCompression;
            BuildOptions &= ~BuildAssetBundleOptions.UncompressedAssetBundle;
            switch (_compressionType)
            {
                case CompressionType.LZ4_ChunkBased: BuildOptions |= BuildAssetBundleOptions.ChunkBasedCompression; break;
                case CompressionType.Uncompressed: BuildOptions |= BuildAssetBundleOptions.UncompressedAssetBundle; break;
            }
        }
        [Serializable] class NodeData { public string outPath; public BuildTarget target; public BuildAssetBundleOptions options; public string manifestName; }

        public override string SaveToJSON()
        {
            UpdateOptionsFromUI();
            return JsonUtility.ToJson(new NodeData { outPath = OutputPath, target = TargetPlatform, options = BuildOptions, manifestName = ManifestName });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                OutputPath = data.outPath;
                TargetPlatform = data.target;
                BuildOptions = data.options;
                ManifestName = string.IsNullOrEmpty(data.manifestName) ? "sys_manifest" : data.manifestName;
                ParseOptionsToUI();

                if (_pathField != null) _pathField.value = OutputPath;
                if (_manifestField != null) _manifestField.value = ManifestName;
                if (_targetField != null) _targetField.value = TargetPlatform;
                if (_compField != null) _compField.value = _compressionType;

                foreach (var kvp in _optionToggles)
                {
                    kvp.Value.value = (BuildOptions & kvp.Key) != 0;
                }
            }
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            if (context.IsBuildMode)
            {
                context.Logs.AppendLine($"[BuildBundleNode] Building AssetBundles...");
                var watch = System.Diagnostics.Stopwatch.StartNew();

                // 加载缓存
                BuildCacheManager.Load();

                // 检查平台变化
                if (BuildCacheManager.Cache.BuildTarget != TargetPlatform.ToString())
                {
                    BuildCacheManager.MarkFullBuildRequired($"Build target changed");
                    BuildCacheManager.Cache.BuildTarget = TargetPlatform.ToString();
                }

                // 按 Bundle 分组资源
                var bundlesByGroup = context.Assets
                    .Where(a => !string.IsNullOrEmpty(a.BundleName))
                    .GroupBy(a => a.BundleName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 检测变化的 Bundle
                var changedBundles = new HashSet<string>();
                foreach (var kvp in bundlesByGroup)
                {
                    var bundleName = kvp.Key;
                    var assets = kvp.Value;

                    // 检查 Bundle 是否需要重建
                    foreach (var asset in assets)
                    {
                        if (BuildCacheManager.Cache.RequiresFullBuild ||
                            !BuildCacheManager.Cache.AssetHashes.TryGetValue(asset.AssetPath, out var cached))
                        {
                            changedBundles.Add(bundleName);
                            break;
                        }

                        // 快速检测
                        var fullPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), asset.AssetPath);
                        if (cached.IsQuickDirty(fullPath))
                        {
                            changedBundles.Add(bundleName);
                            break;
                        }
                    }
                }

                // 依赖传播：检查依赖链
                var affectedByDependency = new HashSet<string>();
                foreach (var bundleName in changedBundles)
                {
                    FindDependentBundles(bundleName, affectedByDependency, bundlesByGroup.Keys.ToList());
                }
                foreach (var bundle in affectedByDependency)
                {
                    changedBundles.Add(bundle);
                }

                // 统计
                int totalBundles = bundlesByGroup.Count;
                int changedCount = changedBundles.Count;
                int skippedCount = totalBundles - changedCount;

                context.Logs.AppendLine($"  Bundles: {changedCount} changed, {skippedCount} skipped (from {totalBundles} total)");

                // 只构建变化的 Bundle
                var assetsToBuild = new List<AssetBuildInfo>();
                foreach (var kvp in bundlesByGroup)
                {
                    if (changedBundles.Contains(kvp.Key))
                    {
                        assetsToBuild.AddRange(kvp.Value);
                    }
                }

                bool success = true;
                if (assetsToBuild.Count > 0 || changedBundles.Count > 0 || !System.IO.Directory.Exists(OutputPath))
                {
                    // 执行构建
                    success = PipelineLauncher.Build(OutputPath, TargetPlatform, BuildOptions, assetsToBuild.Count > 0 ? assetsToBuild : context.Assets, ManifestName);

                    // 更新缓存
                    if (success)
                    {
                        foreach (var asset in assetsToBuild)
                        {
                            BuildCacheManager.UpdateAssetHash(asset.AssetPath, asset.BundleName);
                        }

                        // 更新 Bundle 缓存
                        foreach (var kvp in bundlesByGroup)
                        {
                            var bundlePath = System.IO.Path.Combine(OutputPath, kvp.Key);
                            var deps = new List<string>(); // TODO: 从 manifest 获取依赖
                            BuildCacheManager.UpdateBundleCache(kvp.Key, kvp.Value.Select(a => a.AssetPath).ToList(), deps, bundlePath);
                        }

                        BuildCacheManager.Save();
                    }
                }
                else
                {
                    context.Logs.AppendLine("  All bundles up-to-date, skipping build.");
                }

                watch.Stop();
                long totalSize = 0;
                if (success && System.IO.Directory.Exists(OutputPath))
                {
                    var files = System.IO.Directory.GetFiles(OutputPath, "*", System.IO.SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        // 排除 .manifest 文本文件，只统计二进制
                        if (!f.EndsWith(".manifest"))
                            totalSize += new System.IO.FileInfo(f).Length;
                    }
                }

                // 更新统计
                BuildCacheManager.Cache.Stats.TotalBundles = totalBundles;
                BuildCacheManager.Cache.Stats.RebuiltBundles = changedCount;
                BuildCacheManager.Cache.Stats.SkippedBundles = skippedCount;
                BuildCacheManager.Cache.Stats.BuildTimeMs = watch.ElapsedMilliseconds;

                context.Reports.Add(new BuildReportItem
                {
                    NodeTitle = title,
                    Category = "AssetBundle",
                    OutputPath = OutputPath,
                    AssetCount = context.Assets.Count,
                    OutputSizeBytes = totalSize,
                    DurationSeconds = watch.Elapsed.TotalSeconds,
                    IsSuccess = success,
                    Message = success ? $"OK ({changedCount} rebuilt, {skippedCount} skipped)" : "PipelineLauncher Failed"
                });

                if (success)
                {
                    context.Logs.AppendLine($"  Build Success! Size: {EditorUtility.FormatBytes(totalSize)}");
                    context.Logs.AppendLine(BuildCacheManager.GetBuildStats());
                }
                else context.Logs.AppendLine("  Build Failed!");
            }
            else
            {
                context.Logs.AppendLine($"[BuildBundleNode] Ready to build. (Preview Mode)");
            }

            // 透传数据
            return new Dictionary<string, BuildContext> { { "Pass", context } };
        }

        /// <summary>
        /// 查找依赖于指定Bundle的所有Bundle
        /// </summary>
        private void FindDependentBundles(string bundleName, HashSet<string> result, List<string> allBundleNames)
        {
            // 从缓存中查找依赖关系
            if (BuildCacheManager.Cache.BundleCaches.TryGetValue(bundleName, out var info))
            {
                foreach (var dep in info.Dependencies)
                {
                    if (result.Add(dep))
                    {
                        FindDependentBundles(dep, result, allBundleNames);
                    }
                }
            }
        }
    }
}