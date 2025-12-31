using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System;



#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif

namespace YY
{
    public static class BundleManager
    {
        private static IBundlePathProvider _pathProvider;
        private static AssetBundleManifest _manifest;
        private static Dictionary<string, BundleInfo> _loadedBundles = new Dictionary<string, BundleInfo>();

        // 任务去重 (Request Merging)
        private static Dictionary<string, Task<BundleInfo>> _inflightTasks = new Dictionary<string, Task<BundleInfo>>();

        private static IBundleLoadStrategy _strategy;
        private static RequestScheduler _scheduler;
        private static string _basePath;
#if UNITY_EDITOR
        static int m_SimulateAssetBundleInEditor = -1;
        const string kSimulateAssetBundles = "SimulateInEditor";
        public static bool SimulateInEditor
        {
            get
            {
                if (m_SimulateAssetBundleInEditor == -1)
                    m_SimulateAssetBundleInEditor = EditorPrefs.GetBool(kSimulateAssetBundles, true) ? 1 : 0;

                return m_SimulateAssetBundleInEditor != 0;
            }
            set
            {
                int newValue = value ? 1 : 0;
                if (newValue != m_SimulateAssetBundleInEditor)
                {
                    m_SimulateAssetBundleInEditor = newValue;
                    EditorPrefs.SetBool(kSimulateAssetBundles, value);
                }
            }
        }
#endif

        public static async Task InitializeAsync(string manifestName, IBundlePathProvider provider = null)
        {
            _pathProvider = provider ?? new DefaultBundlePathProvider();
            _strategy = new LocalFileLoadStrategy(); 
            _scheduler = new RequestScheduler(max: 10);

#if UNITY_EDITOR
            if (SimulateInEditor)
            {
                Debug.Log("[BundleManager] Editor Simulation Mode: ON");
                return;
            }
#endif

            var req = await AssetBundle.LoadFromFileAsync(_pathProvider.GetBundlePath(manifestName));
            if (req != null)
            {
                _manifest = req.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            }
            else
            {
                Debug.LogError("[BundleManager] Failed to load Manifest!");
            }
        }

        public static async Task<T> LoadAssetAsync<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (SimulateInEditor)
            {
                string[] paths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(bundleName, assetName);
                if (paths.Length == 0)
                {
                    Debug.LogError($"[Simulate] Asset not found: {assetName} in {bundleName}");
                    return null;
                }
                await Task.Yield();
                return AssetDatabase.LoadAssetAtPath<T>(paths[0]);
            }
#endif

            BundleInfo info = await LoadBundleAsync(bundleName);
            if (info == null || info.Bundle == null) return null;
            var strs = info.Bundle.GetAllAssetNames();
            foreach (var s in strs)
            {
                Debug.Log($"{s}");
            }
            var req = await info.Bundle.LoadAssetAsync(assetName, typeof(T));
            return req as T;
        }

        public static async Task<BundleInfo> LoadBundleAsync(string bundleName)
        {
            if (_loadedBundles.TryGetValue(bundleName, out BundleInfo info))
            {
                info.Retain();
                return info;
            }

            if (_inflightTasks.TryGetValue(bundleName, out var task))
            {
                info = await task;
                if (info != null) info.Retain();
                return info;
            }

            var tcs = LoadBundleInternalAsync(bundleName);
            _inflightTasks.Add(bundleName, tcs);

            try
            {
                info = await tcs;
                if (info != null)
                {
                    info.Retain();
                }
                return info;
            }
            finally
            {
                _inflightTasks.Remove(bundleName);
            }
        }

        private static async Task<BundleInfo> LoadBundleInternalAsync(string bundleName)
        {
            if (_manifest != null)
            {
                string[] deps = _manifest.GetAllDependencies(bundleName);
                if (deps.Length > 0)
                {
                    var depTasks = new List<Task<BundleInfo>>(deps.Length);

                    foreach (var dep in deps)
                    {
                        depTasks.Add(LoadBundleAsync(dep));
                    }
                    await Task.WhenAll(depTasks);
                }
            }

            await _scheduler.WaitSlot();

            try
            {
                if (_loadedBundles.TryGetValue(bundleName, out var info))
                    return info;

                string path = Path.Combine(_pathProvider.GetBundlePath(bundleName));

                //文件存在性检查
                if (!SimulateInEditor && !_pathProvider.Exists(path))
                {
                    throw new BundleLoadException(bundleName, $"File not found at path: {path}");
                }

                AssetBundle bundle = await _strategy.Load(path);
        
                if (bundle == null)
                {
                    throw new BundleLoadException(bundleName, "AssetBundle.LoadFromFileAsync returned null. File might be corrupted.");
                }

                var newInfo = new BundleInfo(bundleName, bundle);
                _loadedBundles[bundleName] = newInfo;
                return newInfo;
            }
            catch (Exception ex)
            {
                // 捕获所有异常（包括依赖加载抛出的），包装一下再抛出，方便调试
                // 避免这里吞掉异常，导致上层一直 await，或者不知道发生了什么
                if (ex is BundleLoadException) throw; // 如果已经是封装好的，直接抛

                throw new BundleLoadException(bundleName, "Unknown error during loading.", ex);
            }
            finally
            {
                _scheduler.ReleaseSlot();
            }
        }

        public static async Task LoadSceneAsync(string bundleName, string sceneName, bool isAdditive)
        {
#if UNITY_EDITOR
            if (SimulateInEditor)
            {
                string[] levelPaths = UnityEditor.AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(bundleName, sceneName);
                if (levelPaths.Length == 0)
                {
                    Debug.LogError($"[Simulate] Scene not found: {sceneName} in {bundleName}");
                    return;
                }

                var mode = isAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single;
                var param = new LoadSceneParameters(mode);
                await EditorSceneManager.LoadSceneAsyncInPlayMode(levelPaths[0], param);
                return;
            }
#endif
            BundleInfo info = await LoadBundleAsync(bundleName);

            if (info == null)
            {
                Debug.LogError($"[BundleManager] Failed to load scene bundle: {bundleName}");
                return;
            }

            var loadMode = isAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            await SceneManager.LoadSceneAsync(sceneName, loadMode);
        }

        public static void UnloadBundle(string bundleName)
        {
#if UNITY_EDITOR
            if (SimulateInEditor) return;
#endif

            if (_loadedBundles.TryGetValue(bundleName, out BundleInfo info))
            {
                if (info.Release())
                {
                    info.Bundle.Unload(true);
                    _loadedBundles.Remove(bundleName);
                    Debug.Log($"[BundleManager] Unloaded: {bundleName}");

                    if (_manifest != null)
                    {
                        string[] deps = _manifest.GetAllDependencies(bundleName);
                        foreach (var dep in deps)
                        {
                            UnloadBundle(dep);
                        }
                    }
                }
            }
        }

        public static void UnloadAll()
        {
            foreach (var kvp in _loadedBundles)
            {
                if (kvp.Value.Bundle != null) kvp.Value.Bundle.Unload(true);
            }
            _loadedBundles.Clear();
            AssetBundle.UnloadAllAssetBundles(true);
            _inflightTasks.Clear();
        }
    }
}