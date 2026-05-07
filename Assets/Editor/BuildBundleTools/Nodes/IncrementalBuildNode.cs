using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;
using Debug = UnityEngine.Debug;

namespace YY.Build.Graph.Nodes
{
    /// <summary>
    /// 增量构建节点
    /// 功能：检测资源变化，过滤出需要重建的资源
    /// 
    /// 输出端口：
    /// - Changed: 变化的资源（需要重建）
    /// - Unchanged: 未变化的资源（可跳过）
    /// - All: 所有资源（透传）
    /// </summary>
    public class IncrementalBuildNode : BaseBuildNode
    {
        #region Settings

        /// <summary>
        /// 是否启用增量构建
        /// </summary>
        public bool EnableIncremental = true;

        /// <summary>
        /// 是否强制全量构建
        /// </summary>
        public bool ForceFullBuild = false;

        /// <summary>
        /// 缓存构建目标平台
        /// </summary>
        public BuildTarget CachedBuildTarget = BuildTarget.StandaloneWindows64;

        #endregion

        #region UI Elements

        private Toggle _incrementalToggle;
        private Toggle _forceFullToggle;
        private EnumField _buildTargetField;
        private Label _statsLabel;

        #endregion

        #region Node Lifecycle

        public override void Initialize()
        {
            base.Initialize();
            title = "Process: Incremental Build";

            // 输入端口
            AddInputPort("Input", Port.Capacity.Multi);

            // 输出端口
            AddOutputPort("Changed", Port.Capacity.Multi);
            AddOutputPort("Unchanged", Port.Capacity.Multi);
            AddOutputPort("All", Port.Capacity.Multi);

            // 设置颜色
            titleContainer.style.backgroundColor = new Color(0.2f, 0.5f, 0.6f);

            DrawUI();
        }

        private void DrawUI()
        {
            var container = new VisualElement
            {
                style = { paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5 }
            };

            // 增量构建开关
            _incrementalToggle = new Toggle("Incremental Build") { value = EnableIncremental };
            _incrementalToggle.RegisterValueChangedCallback(e =>
            {
                EnableIncremental = e.newValue;
                NotifyChange();
            });

            // 强制全量构建
            _forceFullToggle = new Toggle("Force Full Build") { value = ForceFullBuild };
            _forceFullToggle.RegisterValueChangedCallback(e =>
            {
                ForceFullBuild = e.newValue;
                NotifyChange();
            });

            // 构建目标
            _buildTargetField = new EnumField("Cache Target:", CachedBuildTarget);
            _buildTargetField.RegisterValueChangedCallback(e =>
            {
                CachedBuildTarget = (BuildTarget)e.newValue;
                NotifyChange();
            });

            // 统计信息
            _statsLabel = new Label("No data yet")
            {
                style = { color = Color.gray, fontSize = 10, marginTop = 5 }
            };

            // 清除缓存按钮
            var clearCacheBtn = new Button(() =>
            {
                BuildCacheManager.Clear();
                Debug.Log("[IncrementalBuildNode] Cache cleared.");
                _statsLabel.text = "Cache cleared. Next build will be full.";
            })
            { text = "Clear Cache", style = { marginTop = 5 } };

            container.Add(_incrementalToggle);
            container.Add(_forceFullToggle);
            container.Add(_buildTargetField);
            container.Add(_statsLabel);
            container.Add(clearCacheBtn);

            mainContainer.Add(container);
        }

        #endregion

        #region Execution

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine("[IncrementalBuildNode] Processing...");

            // 加载缓存
            BuildCacheManager.Load();

            // 检查平台变化
            if (BuildCacheManager.Cache.BuildTarget != CachedBuildTarget.ToString())
            {
                BuildCacheManager.MarkFullBuildRequired($"Build target changed ({BuildCacheManager.Cache.BuildTarget} -> {CachedBuildTarget})");
                BuildCacheManager.Cache.BuildTarget = CachedBuildTarget.ToString();
            }

            // 初始化输出上下文
            var changedCtx = new BuildContext();
            var unchangedCtx = new BuildContext();
            var allCtx = new BuildContext();

            // 复制日志
            changedCtx.Logs.Append(context.Logs);
            unchangedCtx.Logs.Append(context.Logs);
            allCtx.Logs.Append(context.Logs);

            // 全量构建检查
            if (!EnableIncremental || ForceFullBuild || BuildCacheManager.Cache.RequiresFullBuild)
            {
                string reason = !EnableIncremental ? "Incremental disabled" :
                               ForceFullBuild ? "Force full build" :
                               "Cache requires full build";

                context.Logs.AppendLine($"  Full build: {reason}");

                // 所有资源都视为变化
                foreach (var asset in context.Assets)
                {
                    changedCtx.Assets.Add(asset);
                    allCtx.Assets.Add(asset);
                }

                _statsLabel.text = $"Full build: {context.Assets.Count} assets";
            }
            else
            {
                // 增量检测
                var sw = Stopwatch.StartNew();

                var assetPaths = context.Assets.Select(a => a.AssetPath).ToList();
                var changedAssets = BuildCacheManager.DetectChangedAssets(assetPaths);
                var affectedBundles = BuildCacheManager.CalculateAffectedBundles(changedAssets);

                sw.Stop();

                // 分流资源
                foreach (var asset in context.Assets)
                {
                    allCtx.Assets.Add(asset);

                    // 检查资源所属的Bundle是否受影响
                    if (changedAssets.Contains(asset.AssetPath) ||
                        (asset.BundleName != null && affectedBundles.Contains(asset.BundleName)))
                    {
                        changedCtx.Assets.Add(asset);
                    }
                    else
                    {
                        unchangedCtx.Assets.Add(asset);
                    }
                }

                // 更新统计
                BuildCacheManager.Cache.Stats.DetectionTimeMs = sw.ElapsedMilliseconds;

                context.Logs.AppendLine($"  Detected {changedAssets.Count} changed assets");
                context.Logs.AppendLine($"  Affected {affectedBundles.Count} bundles");
                context.Logs.AppendLine($"  Changed: {changedCtx.Assets.Count}, Unchanged: {unchangedCtx.Assets.Count}");

                _statsLabel.text = $"Changed: {changedCtx.Assets.Count}, Unchanged: {unchangedCtx.Assets.Count}";
            }

            return new Dictionary<string, BuildContext>
            {
                { "Changed", changedCtx },
                { "Unchanged", unchangedCtx },
                { "All", allCtx }
            };
        }

        #endregion

        #region Serialization

        [System.Serializable]
        private class NodeData
        {
            public bool enableIncremental;
            public bool forceFullBuild;
            public BuildTarget buildTarget;
        }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData
            {
                enableIncremental = EnableIncremental,
                forceFullBuild = ForceFullBuild,
                buildTarget = CachedBuildTarget
            });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data == null) return;

            EnableIncremental = data.enableIncremental;
            ForceFullBuild = data.forceFullBuild;
            CachedBuildTarget = data.buildTarget;

            if (_incrementalToggle != null) _incrementalToggle.value = EnableIncremental;
            if (_forceFullToggle != null) _forceFullToggle.value = ForceFullBuild;
            if (_buildTargetField != null) _buildTargetField.value = CachedBuildTarget;
        }

        #endregion
    }
}