using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;


#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif

namespace YY
{
    public static class BundleManager
    {
        public delegate string OverrideBaseDownloadingURLDelegate(string bundleName);

        public static OverrideBaseDownloadingURLDelegate overrideBaseDownloadingURL;
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
        public static string GetAssetBundleBaseDownloadingURL(string bundleName)
        {
            if (overrideBaseDownloadingURL != null)
            {
                string res = overrideBaseDownloadingURL.Invoke(bundleName);
                if (res != null)
                    return res;
            }
            return _basePath;
        }

        public static async Task InitializeAsync(string manifestName)
        {
            _strategy = new LocalFileLoadStrategy(); 
            _scheduler = new RequestScheduler(max: 10);

#if UNITY_EDITOR
            if (SimulateInEditor)
            {
                Debug.Log("[BundleManager] Editor Simulation Mode: ON");
                return;
            }
#endif

            var req = await AssetBundle.LoadFromFileAsync(GetAssetBundleBaseDownloadingURL(manifestName) + manifestName);
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

                string path = Path.Combine(GetAssetBundleBaseDownloadingURL(bundleName), bundleName);
                AssetBundle bundle = await _strategy.Load(path);

                if (bundle == null) return null;

                var newInfo = new BundleInfo(bundleName, bundle);
                _loadedBundles[bundleName] = newInfo;
                return newInfo;
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