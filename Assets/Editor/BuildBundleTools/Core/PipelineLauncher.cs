using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;

namespace YY.Build
{
    public static class PipelineLauncher
    {
        /// <summary>
        /// 核心打包入口
        /// </summary>
        /// <param name="context">打包配置</param>
        /// <param name="assets">经过节点处理后的最终资产列表</param>
        public static bool Build(BuildGraphContext context, List<AssetBuildInfo> assets)
        {
            // 0. 基础校验
            if (assets == null || assets.Count == 0)
            {
                Debug.LogWarning("[PipelineLauncher] No assets to build.");
                return false;
            }

            // 1. 准备输出目录
            if (!Directory.Exists(context.OutputPath))
                Directory.CreateDirectory(context.OutputPath);

            // 2. 数据转换：List<AssetBuildInfo> -> AssetBundleBuild[]
            // 这是关键步骤：将散乱的资产按 BundleName 聚合
            var buildMap = ConvertToBuildMap(assets);

            if (buildMap.Length == 0)
            {
                Debug.LogError("[PipelineLauncher] No valid bundles found after grouping. Did you forget to set BundleNames?");
                return false;
            }

            // 3. 构建 SBP 参数
            // SBP 需要 BundleBuildParameters 来配置压缩、平台等
            var buildParams = new BundleBuildParameters(context.TargetPlatform, context.TargetGroup, context.OutputPath);

            // 设置压缩格式
            if ((context.Options & BuildAssetBundleOptions.ChunkBasedCompression) != 0)
                buildParams.BundleCompression = BuildCompression.LZ4;
            else if ((context.Options & BuildAssetBundleOptions.UncompressedAssetBundle) != 0)
                buildParams.BundleCompression = BuildCompression.Uncompressed;
            else
                buildParams.BundleCompression = BuildCompression.LZMA;

            // 4. 执行 SBP 打包
            Debug.Log($"[PipelineLauncher] Start building {buildMap.Length} bundles...");

            IBundleBuildResults results;
            ReturnCode exitCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(buildMap), out results);

            // 5. 结果处理
            if (exitCode < ReturnCode.Success)
            {
                Debug.LogError($"[PipelineLauncher] Build Failed! Code: {exitCode}");
                return false;
            }
            else
            {
                Debug.Log($"[PipelineLauncher] Build Success! Output: {context.OutputPath}");
                return true;
            }
        }

        /// <summary>
        /// 将扁平的 AssetBuildInfo 列表转换为 Unity 原生的 AssetBundleBuild 结构
        /// </summary>
        private static AssetBundleBuild[] ConvertToBuildMap(List<AssetBuildInfo> assets)
        {
            // 过滤掉没有 BundleName 的无效资产
            var validAssets = assets.Where(a => !string.IsNullOrEmpty(a.BundleName));

            // 使用 Linq 进行分组：Key是包名，Value是该包里所有的资产
            var grouped = validAssets.GroupBy(a => a.BundleName);

            var buildList = new List<AssetBundleBuild>();

            foreach (var group in grouped)
            {
                var build = new AssetBundleBuild();
                build.assetBundleName = group.Key;

                // 获取该包内所有资产的路径
                build.assetNames = group.Select(a => a.AssetPath).ToArray();

                // 获取该包内所有资产的 Address
                build.addressableNames = group.Select(a => a.AddressableName).ToArray();

                buildList.Add(build);
            }

            return buildList.ToArray();
        }
    }
}