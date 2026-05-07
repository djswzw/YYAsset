using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YY.Build.Core
{
    /// <summary>
    /// 构建缓存管理器
    /// 负责缓存的加载、保存、变更检测和Bundle影响分析
    /// </summary>
    public static class BuildCacheManager
    {
        #region Constants

        private const string kCacheFileName = "BuildCache.json";
        private const string kCacheFolderPath = "Library/BuildCache";

        #endregion

        #region Properties

        /// <summary>
        /// 当前缓存数据
        /// </summary>
        public static BuildCacheData Cache { get; private set; } = new BuildCacheData();

        /// <summary>
        /// 缓存文件完整路径
        /// </summary>
        public static string CacheFilePath => Path.Combine(kCacheFolderPath, kCacheFileName);

        /// <summary>
        /// 是否已加载缓存
        /// </summary>
        public static bool IsLoaded { get; private set; }

        #endregion

        #region Load/Save

        /// <summary>
        /// 加载缓存
        /// </summary>
        public static void Load()
        {
            if (IsLoaded)
            {
                return;
            }

            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    Cache = JsonUtility.FromJson<BuildCacheData>(json);

                    // 版本兼容性检查
                    if (Cache.Version != BuildCacheData.kCacheVersion)
                    {
                        Debug.Log($"[BuildCacheManager] Cache version mismatch ({Cache.Version} != {BuildCacheData.kCacheVersion}), clearing cache.");
                        Cache = new BuildCacheData();
                    }

                    // Unity版本变化需要全量构建
                    if (Cache.UnityVersion != Application.unityVersion)
                    {
                        Debug.Log($"[BuildCacheManager] Unity version changed ({Cache.UnityVersion} -> {Application.unityVersion}), full rebuild required.");
                        Cache.RequiresFullBuild = true;
                    }

                    Debug.Log($"[BuildCacheManager] Cache loaded: {Cache.AssetHashes.Count} assets, {Cache.BundleCaches.Count} bundles");
                }
                else
                {
                    Cache = new BuildCacheData();
                    Debug.Log("[BuildCacheManager] No cache found, starting fresh.");
                }

                IsLoaded = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildCacheManager] Failed to load cache: {e.Message}");
                Cache = new BuildCacheData();
                IsLoaded = true;
            }
        }

        /// <summary>
        /// 保存缓存
        /// </summary>
        public static void Save()
        {
            try
            {
                // 确保目录存在
                if (!Directory.Exists(kCacheFolderPath))
                {
                    Directory.CreateDirectory(kCacheFolderPath);
                }

                Cache.LastBuildTimestamp = DateTime.UtcNow.Ticks;
                Cache.UnityVersion = Application.unityVersion;

                var json = JsonUtility.ToJson(Cache, true);
                File.WriteAllText(CacheFilePath, json);

                Debug.Log($"[BuildCacheManager] Cache saved: {Cache.AssetHashes.Count} assets, {Cache.BundleCaches.Count} bundles");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildCacheManager] Failed to save cache: {e.Message}");
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public static void Clear()
        {
            Cache.Clear();
            if (File.Exists(CacheFilePath))
            {
                File.Delete(CacheFilePath);
            }
            Debug.Log("[BuildCacheManager] Cache cleared.");
        }

        #endregion

        #region Change Detection

        /// <summary>
        /// 检测变化的资源
        /// </summary>
        /// <param name="assetPaths">要检测的资源路径列表</param>
        /// <returns>变化的资源路径集合</returns>
        public static HashSet<string> DetectChangedAssets(List<string> assetPaths)
        {
            var sw = Stopwatch.StartNew();
            var changedAssets = new HashSet<string>();

            foreach (var assetPath in assetPaths)
            {
                if (IsAssetChanged(assetPath))
                {
                    changedAssets.Add(assetPath);
                }
            }

            sw.Stop();
            Cache.Stats.DetectionTimeMs = sw.ElapsedMilliseconds;
            Cache.Stats.TotalAssets = assetPaths.Count;
            Cache.Stats.ChangedAssets = changedAssets.Count;

            Debug.Log($"[BuildCacheManager] Detected {changedAssets.Count}/{assetPaths.Count} changed assets in {sw.ElapsedMilliseconds}ms");

            return changedAssets;
        }

        /// <summary>
        /// 检测单个资源是否变化
        /// </summary>
        private static bool IsAssetChanged(string assetPath)
        {
            // 标准化路径
            assetPath = assetPath.Replace("\\", "/");

            // 新资源
            if (!Cache.AssetHashes.TryGetValue(assetPath, out var cachedInfo))
            {
                return true;
            }

            // 快速检测：文件是否存在
            var fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), assetPath);
            if (!File.Exists(fullPath))
            {
                // 文件被删除
                return true;
            }

            // 快速检测：大小和修改时间
            if (!cachedInfo.IsQuickDirty(fullPath))
            {
                return false;
            }

            // 精确检测：内容Hash
            var currentHash = AssetHashInfo.ComputeHash(fullPath);
            return cachedInfo.ContentHash != currentHash;
        }

        /// <summary>
        /// 计算受影响的Bundle
        /// </summary>
        /// <param name="changedAssets">变化的资源集合</param>
        /// <returns>需要重建的Bundle集合</returns>
        public static HashSet<string> CalculateAffectedBundles(HashSet<string> changedAssets)
        {
            var affectedBundles = new HashSet<string>();

            // 1. 直接包含变化资源的Bundle
            foreach (var bundleInfo in Cache.BundleCaches.Values)
            {
                foreach (var assetPath in bundleInfo.AssetPaths)
                {
                    if (changedAssets.Contains(assetPath))
                    {
                        affectedBundles.Add(bundleInfo.BundleName);
                        break;
                    }
                }
            }

            // 2. 依赖传播：依赖这些Bundle的其他Bundle也需要重建
            var additionalAffected = new HashSet<string>();
            foreach (var bundleName in affectedBundles)
            {
                FindDependentBundles(bundleName, additionalAffected);
            }

            // 合并结果
            foreach (var bundle in additionalAffected)
            {
                affectedBundles.Add(bundle);
            }

            Cache.Stats.TotalBundles = Cache.BundleCaches.Count;
            Cache.Stats.RebuiltBundles = affectedBundles.Count;
            Cache.Stats.SkippedBundles = Cache.BundleCaches.Count - affectedBundles.Count;

            Debug.Log($"[BuildCacheManager] Affected bundles: {affectedBundles.Count}/{Cache.BundleCaches.Count}");

            return affectedBundles;
        }

        /// <summary>
        /// 查找依赖于指定Bundle的所有Bundle
        /// </summary>
        private static void FindDependentBundles(string bundleName, HashSet<string> result)
        {
            foreach (var bundleInfo in Cache.BundleCaches.Values)
            {
                if (bundleInfo.Dependencies.Contains(bundleName))
                {
                    if (result.Add(bundleInfo.BundleName))
                    {
                        // 递归查找
                        FindDependentBundles(bundleInfo.BundleName, result);
                    }
                }
            }
        }

        #endregion

        #region Cache Update

        /// <summary>
        /// 更新资源Hash缓存
        /// </summary>
        public static void UpdateAssetHash(string assetPath, string bundleName)
        {
            assetPath = assetPath.Replace("\\", "/");
            var fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), assetPath);

            if (!File.Exists(fullPath))
            {
                Cache.AssetHashes.Remove(assetPath);
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            var info = new AssetHashInfo
            {
                AssetPath = assetPath,
                ContentHash = AssetHashInfo.ComputeHash(fullPath),
                FileSize = fileInfo.Length,
                LastWriteTime = fileInfo.LastWriteTimeUtc.Ticks,
                BundleName = bundleName
            };

            Cache.AssetHashes[assetPath] = info;
        }

        /// <summary>
        /// 更新Bundle缓存
        /// </summary>
        public static void UpdateBundleCache(string bundleName, List<string> assetPaths, List<string> dependencies, string bundleFilePath)
        {
            var info = new BundleCacheInfo
            {
                BundleName = bundleName,
                AssetPaths = assetPaths.ToList(),
                Dependencies = dependencies.ToList(),
                BuildTimestamp = DateTime.UtcNow.Ticks
            };

            if (File.Exists(bundleFilePath))
            {
                var fileInfo = new FileInfo(bundleFilePath);
                info.BundleSize = fileInfo.Length;
                info.BundleHash = AssetHashInfo.ComputeHash(bundleFilePath);
            }

            Cache.BundleCaches[bundleName] = info;

            // 更新每个资源的Bundle引用
            foreach (var assetPath in assetPaths)
            {
                UpdateAssetHash(assetPath, bundleName);
            }
        }

        /// <summary>
        /// 更新依赖缓存
        /// </summary>
        public static void UpdateDependencyCache(string assetPath, string[] dependencies)
        {
            Cache.DependencyCache[assetPath] = dependencies;
        }

        #endregion

        #region Utility

        /// <summary>
        /// 获取构建统计信息
        /// </summary>
        public static string GetBuildStats()
        {
            return Cache.Stats.ToString();
        }

        /// <summary>
        /// 检查Bundle是否需要重建
        /// </summary>
        public static bool NeedsRebuild(string bundleName)
        {
            if (Cache.RequiresFullBuild)
            {
                return true;
            }

            return Cache.AffectedBundles.Contains(bundleName);
        }

        /// <summary>
        /// 标记需要全量构建
        /// </summary>
        public static void MarkFullBuildRequired(string reason)
        {
            Cache.RequiresFullBuild = true;
            Debug.Log($"[BuildCacheManager] Full build required: {reason}");
        }

        #endregion
    }
}