using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;
using UnityEngine.Build.Pipeline;

namespace YY.Build.Core
{
    public static class PipelineLauncher
    {
        // 增加 manifestName 参数
        public static bool Build(string outputPath, BuildTarget target, BuildAssetBundleOptions options, List<AssetBuildInfo> assets, string manifestName)
        {
            if (assets == null || assets.Count == 0) return false;
            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
            if (string.IsNullOrEmpty(manifestName)) manifestName = "sys_manifest";

            var buildMap = ConvertToBuildMap(assets);
            if (buildMap.Length == 0) return false;

            Debug.Log($"[PipelineLauncher] Step 1: Building Content Bundles...");

            // 1. 第一次打包：构建所有资源
            var manifest = CompatibilityBuildPipeline.BuildAssetBundles(outputPath, buildMap, options, target);

            if (manifest != null)
            {
                // 2. 第二次打包：将 Manifest 对象打包成二进制 Bundle
                Debug.Log($"[PipelineLauncher] Step 2: Building Binary Manifest [{manifestName}]...");
                bool manifestBuildSuccess = BuildManifestAsBundle(manifest, outputPath, manifestName, options, target);

                if (manifestBuildSuccess)
                {
                    // 3. 清理 SBP 自动生成的那个纯文本 manifest 文件 (和文件夹同名的那个)
                    // 因为我们已经生成了自定义名字的二进制 Manifest Bundle，那个默认的文本文件可能会造成混淆
                    string defaultManifestPath = Path.Combine(outputPath, Path.GetFileName(outputPath) + ".manifest");
                    if (File.Exists(defaultManifestPath)) File.Delete(defaultManifestPath);

                    Debug.Log($"[PipelineLauncher] All Success! Output: {outputPath}");
                    return true;
                }
            }

            Debug.LogError("[PipelineLauncher] Build Failed.");
            return false;
        }

        /// <summary>
        /// 将 Manifest ScriptableObject 打包成二进制 AssetBundle
        /// </summary>
        private static bool BuildManifestAsBundle(CompatibilityAssetBundleManifest manifest, string outputPath, string bundleName, BuildAssetBundleOptions options, BuildTarget target)
        {
            // A. 创建临时目录存放 .asset 文件
            // 必须在 Assets 目录下，否则 SBP 无法识别
            string tempDir = "Assets/YY_Build_Temp";
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string tempAssetPath = $"{tempDir}/{bundleName}_raw.asset";

            try
            {
                // B. 将内存中的 Manifest 对象保存为物理文件
                AssetDatabase.CreateAsset(manifest, tempAssetPath);

                // C. 构建针对这个 Manifest 的打包任务
                var build = new AssetBundleBuild();
                build.assetBundleName = bundleName; // 这里就是你要的无后缀文件名 (或加 .bundle，随你)
                build.assetNames = new[] { tempAssetPath };

                // D. 构造 SBP 参数
                var buildParams = new BundleBuildParameters(target, BuildPipeline.GetBuildTargetGroup(target), outputPath);

                // 保持和主包一样的压缩格式
                if ((options & BuildAssetBundleOptions.ChunkBasedCompression) != 0)
                    buildParams.BundleCompression = BuildCompression.LZ4;
                else if ((options & BuildAssetBundleOptions.UncompressedAssetBundle) != 0)
                    buildParams.BundleCompression = BuildCompression.Uncompressed;
                else
                    buildParams.BundleCompression = BuildCompression.LZMA;

                // E. 执行单独打包
                IBundleBuildResults results;
                ReturnCode code = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(new[] { build }), out results);

                if (code < ReturnCode.Success)
                {
                    Debug.LogError($"[PipelineLauncher] Failed to build manifest bundle: {code}");
                    return false;
                }

                return true;
            }
            finally
            {
                // F. 清理临时文件
                AssetDatabase.DeleteAsset(tempAssetPath);
                AssetDatabase.DeleteAsset(tempDir);
                AssetDatabase.Refresh();
            }
        }

        private static AssetBundleBuild[] ConvertToBuildMap(List<AssetBuildInfo> assets)
        {
            return assets.Where(a => !string.IsNullOrEmpty(a.BundleName))
                .GroupBy(a => a.BundleName)
                .Select(g => new AssetBundleBuild
                {
                    assetBundleName = g.Key,
                    assetNames = g.Select(a => a.AssetPath).ToArray(),
                    addressableNames = g.Select(a => Path.GetFileNameWithoutExtension(a.AssetPath)).ToArray()
                }).ToArray();
        }
    }
}