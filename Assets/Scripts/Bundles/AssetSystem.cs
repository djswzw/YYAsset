using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YY
{
    public static class AssetSystem
    {
        private static Dictionary<string, AssetInternalNode> _nodes = new Dictionary<string, AssetInternalNode>();

        public static async Task<AssetHandle<T>> LoadAsync<T>(string bundleName, string assetName, int timeoutSeconds = 10) where T : UnityEngine.Object
        {
            string key = $"{bundleName}/{assetName}";

            AssetInternalNode node = null;
            try
            {
                // 1. 查找或创建节点
                if (!_nodes.TryGetValue(key, out node))
                {
                    node = new AssetInternalNode
                    {
                        BundleName = bundleName,
                        AssetName = assetName,
                        RefCount = 0
                    };
                    _nodes.Add(key, node);
                    node.LoadingTask = LoadInternal(node);
                }

                // 2. 超时处理机制
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                // 竞速：看谁先完成（加载任务 vs 计时任务）
                var completedTask = await Task.WhenAny(node.LoadingTask, timeoutTask);

                // 如果计时任务先完成，说明超时了
                if (completedTask == timeoutTask)
                {
                    Debug.LogError($"[AssetSystem] Load Timeout: {key} ({timeoutSeconds}s)");
                    // 注意：底层的 IO 任务还在跑，但我们不再关心结果，也不增加引用计数
                    _nodes.Remove(key);
                    return null;
                }

                // 3. 等待加载结果并接收异常
                // 虽然上面 WhenAny 结束了，但如果 LoadInternal 抛出了异常，必须再次 await 才能捕获到
                await node.LoadingTask;

                // 4. 校验资源有效性
                if (node.TargetAsset == null)
                {
                    // 虽然没抛异常，但可能因为 Bundle 里没这个资源导致加载为空
                    _nodes.Remove(key);
                    return null;
                }
                node.RefCount++;

                // 5. 创建并返回句柄
                var handle = new AssetHandle<T>(key, (h) => ReleaseNode(key));
                handle.Asset = node.TargetAsset as T;
                return handle;
            }
            catch (BundleLoadException ex)
            {
                Debug.LogError($"[AssetSystem] Failed to load asset '{key}': {ex.Message}");
                _nodes.Remove(key); 
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssetSystem] Unexpected exception: {ex}");
                _nodes.Remove(key);
                return null;
            }
        }

        private static async Task LoadInternal(AssetInternalNode node)
        {
            node.TargetAsset = await BundleManager.LoadAssetAsync<UnityEngine.Object>(node.BundleName, node.AssetName);
        }

        public static AssetHandle<T> TryGetLoadedAsset<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
            string key = $"{bundleName}/{assetName}";
            if (_nodes.TryGetValue(key, out var node) && node.TargetAsset != null)
            {
                node.RefCount++;
                var handle = new AssetHandle<T>(key, (h) => ReleaseNode(key));
                handle.Asset = node.TargetAsset as T;
                return handle;
            }
            return null;
        }

        private static void ReleaseNode(string key)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                node.Release();
                if (node.RefCount <= 0) _nodes.Remove(key);
            }
        }

        public static async void LoadAndBind<T>(UnityEngine.Object binder, string bundleName, string assetName, Action<T> callback) where T : UnityEngine.Object
        {
            if (binder == null) return;

            var handle = await LoadAsync<T>(bundleName, assetName);

            if (binder is GameObject go) AddBinderComponent(go, handle);
            else if (binder is Component comp) AddBinderComponent(comp.gameObject, handle);

            callback?.Invoke(handle.Asset);
        }

        private static void AddBinderComponent(GameObject go, IDisposable handle)
        {
            var listener = go.AddComponent<AssetBindingListener>();
            listener.Handle = handle;
        }

#if UNITY_EDITOR
        public static List<AssetInternalNode> GetLoadedAssetNodes()
        {
            return new List<AssetInternalNode>(_nodes.Values);
        }

        public static List<string> GetLoadedAssetsInBundle(string bundleName)
        {
            var result = new List<string>();
            foreach (var node in _nodes.Values)
            {
                if (node.BundleName == bundleName)
                {
                    result.Add(node.AssetName);
                }
            }
            return result;
        }
#endif
    }

    internal class AssetBindingListener : MonoBehaviour
    {
        public IDisposable Handle;
        private void OnDestroy() => Handle?.Dispose();
    }
}