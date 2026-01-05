using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;
using YY.Build.Data; // 引用 BuildContext

namespace YY.Build.Core
{
    public static class PipelineLauncher
    {
        public static bool Build(string outputPath, BuildTarget target, BuildAssetBundleOptions options, List<AssetBuildInfo> assets)
        {
            // 1. 基础校验
            if (assets == null || assets.Count == 0)
            {
                Debug.LogError("[PipelineLauncher] 没有任何资产需要打包！");
                return false;
            }

            // 2. 准备输出目录
            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

            // 3. 数据转换：List<AssetBuildInfo> -> AssetBundleBuild[]
            var buildMap = ConvertToBuildMap(assets);
            if (buildMap.Length == 0)
            {
                Debug.LogError("[PipelineLauncher] 资产列表不为空，但没有有效的 BundleName 分组。请检查 GrouperNode 设置。");
                return false;
            }

            // 4. 构建参数
            var buildParams = new BundleBuildParameters(target, BuildPipeline.GetBuildTargetGroup(target), outputPath);

            // 设置压缩格式
            if ((options & BuildAssetBundleOptions.ChunkBasedCompression) != 0)
                buildParams.BundleCompression = BuildCompression.LZ4;
            else if ((options & BuildAssetBundleOptions.UncompressedAssetBundle) != 0)
                buildParams.BundleCompression = BuildCompression.Uncompressed;
            else
                buildParams.BundleCompression = BuildCompression.LZMA; // 默认

            // 5. 执行 SBP 打包
            Debug.Log($"[PipelineLauncher] 开始打包 {buildMap.Length} 个 Bundle 到: {outputPath} ...");

            IBundleBuildResults results;
            ReturnCode exitCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(buildMap), out results);

            // 6. 结果处理
            if (exitCode < ReturnCode.Success)
            {
                Debug.LogError($"[PipelineLauncher] 打包失败! Code: {exitCode}");
                return false;
            }
            else
            {
                Debug.Log($"[PipelineLauncher] 打包成功! \n输出路径: {outputPath}");
                // 简单的生成一个 Manifest 供运行时读取 (SBP 默认生成的 manifest 文件名可能较长)
                // 这里可以扩展生成自己的资源清单逻辑
                return true;
            }
        }

        private static AssetBundleBuild[] ConvertToBuildMap(List<AssetBuildInfo> assets)
        {
            // 过滤掉没有 BundleName 的无效资产
            var validAssets = assets.Where(a => !string.IsNullOrEmpty(a.BundleName));

            // 分组
            var grouped = validAssets.GroupBy(a => a.BundleName);
            var buildList = new List<AssetBundleBuild>();

            foreach (var group in grouped)
            {
                var build = new AssetBundleBuild();
                build.assetBundleName = group.Key;
                build.assetNames = group.Select(a => a.AssetPath).ToArray();
                // 默认 Addressable Name 使用文件名，也可以扩展 logic 让节点配置
                build.addressableNames = group.Select(a => System.IO.Path.GetFileNameWithoutExtension(a.AssetPath)).ToArray();
                buildList.Add(build);
            }

            return buildList.ToArray();
        }
    }
}