using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace YY.Build.Core
{
    /// <summary>
    /// 资源Hash信息
    /// 记录单个资源的缓存状态
    /// </summary>
    [Serializable]
    public class AssetHashInfo
    {
        /// <summary>
        /// 资源路径（相对于Assets目录）
        /// </summary>
        public string AssetPath;

        /// <summary>
        /// 内容Hash（MD5）
        /// </summary>
        public string ContentHash;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSize;

        /// <summary>
        /// 最后修改时间（Ticks）
        /// </summary>
        public long LastWriteTime;

        /// <summary>
        /// 所属Bundle名称
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 快速检测：文件大小或修改时间是否变化
        /// </summary>
        public bool IsQuickDirty(string currentPath)
        {
            if (!File.Exists(currentPath))
            {
                return true;
            }

            var fileInfo = new FileInfo(currentPath);
            return fileInfo.Length != FileSize || fileInfo.LastWriteTimeUtc.Ticks != LastWriteTime;
        }

        /// <summary>
        /// 计算当前文件的Hash
        /// </summary>
        public static string ComputeHash(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Bundle构建信息
    /// 记录单个Bundle的缓存状态
    /// </summary>
    [Serializable]
    public class BundleCacheInfo
    {
        /// <summary>
        /// Bundle名称
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 包含的资源路径列表
        /// </summary>
        public List<string> AssetPaths = new List<string>();

        /// <summary>
        /// 依赖的Bundle列表
        /// </summary>
        public List<string> Dependencies = new List<string>();

        /// <summary>
        /// 构建时间戳
        /// </summary>
        public long BuildTimestamp;

        /// <summary>
        /// Bundle文件大小
        /// </summary>
        public long BundleSize;

        /// <summary>
        /// Bundle文件Hash
        /// </summary>
        public string BundleHash;
    }

    /// <summary>
    /// 构建缓存数据
    /// 存储整个构建过程的状态
    /// </summary>
    [Serializable]
    public class BuildCacheData
    {
        /// <summary>
        /// 缓存版本号（用于兼容性检测）
        /// </summary>
        public const int kCacheVersion = 1;

        /// <summary>
        /// 缓存版本
        /// </summary>
        public int Version = kCacheVersion;

        /// <summary>
        /// 最后构建时间戳
        /// </summary>
        public long LastBuildTimestamp;

        /// <summary>
        /// 最后构建的Unity版本
        /// </summary>
        public string UnityVersion;

        /// <summary>
        /// 目标构建平台
        /// </summary>
        public string BuildTarget;

        /// <summary>
        /// 资源Hash映射：AssetPath -> AssetHashInfo
        /// </summary>
        public Dictionary<string, AssetHashInfo> AssetHashes = new Dictionary<string, AssetHashInfo>();

        /// <summary>
        /// Bundle缓存映射：BundleName -> BundleCacheInfo
        /// </summary>
        public Dictionary<string, BundleCacheInfo> BundleCaches = new Dictionary<string, BundleCacheInfo>();

        /// <summary>
        /// 依赖关系缓存：AssetPath -> 依赖路径列表
        /// </summary>
        public Dictionary<string, string[]> DependencyCache = new Dictionary<string, string[]>();

        /// <summary>
        /// 变化的资源列表（运行时填充）
        /// </summary>
        [NonSerialized]
        public HashSet<string> ChangedAssets = new HashSet<string>();

        /// <summary>
        /// 受影响的Bundle列表（运行时填充）
        /// </summary>
        [NonSerialized]
        public HashSet<string> AffectedBundles = new HashSet<string>();

        /// <summary>
        /// 是否需要全量构建
        /// </summary>
        [NonSerialized]
        public bool RequiresFullBuild = false;

        /// <summary>
        /// 增量构建统计
        /// </summary>
        [NonSerialized]
        public IncrementalBuildStats Stats = new IncrementalBuildStats();

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void Clear()
        {
            AssetHashes.Clear();
            BundleCaches.Clear();
            DependencyCache.Clear();
            ChangedAssets.Clear();
            AffectedBundles.Clear();
            RequiresFullBuild = false;
            Stats = new IncrementalBuildStats();
        }
    }

    /// <summary>
    /// 增量构建统计信息
    /// </summary>
    [Serializable]
    public class IncrementalBuildStats
    {
        /// <summary>
        /// 总资源数
        /// </summary>
        public int TotalAssets;

        /// <summary>
        /// 变化的资源数
        /// </summary>
        public int ChangedAssets;

        /// <summary>
        /// 总Bundle数
        /// </summary>
        public int TotalBundles;

        /// <summary>
        /// 需要重建的Bundle数
        /// </summary>
        public int RebuiltBundles;

        /// <summary>
        /// 跳过的Bundle数（无变化）
        /// </summary>
        public int SkippedBundles;

        /// <summary>
        /// 检测耗时（毫秒）
        /// </summary>
        public long DetectionTimeMs;

        /// <summary>
        /// 构建耗时（毫秒）
        /// </summary>
        public long BuildTimeMs;

        /// <summary>
        /// 缓存命中率
        /// </summary>
        public float CacheHitRate => TotalBundles > 0 ? (float)SkippedBundles / TotalBundles : 0f;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Incremental Build Stats]");
            sb.AppendLine($"  Assets: {ChangedAssets}/{TotalAssets} changed ({(TotalAssets > 0 ? (float)ChangedAssets / TotalAssets * 100 : 0):F1}%)");
            sb.AppendLine($"  Bundles: {RebuiltBundles} rebuilt, {SkippedBundles} skipped ({CacheHitRate * 100:F1}% hit rate)");
            sb.AppendLine($"  Time: Detection={DetectionTimeMs}ms, Build={BuildTimeMs}ms");
            return sb.ToString();
        }
    }
}