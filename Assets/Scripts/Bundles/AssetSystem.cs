using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YY
{
    public static class AssetSystem
    {
        private static Dictionary<string, AssetInternalNode> _nodes = new Dictionary<string, AssetInternalNode>();

        public static async Task<AssetHandle<T>> LoadAsync<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
            string key = $"{bundleName}/{assetName}";

            if (!_nodes.TryGetValue(key, out var node))
            {
                node = new AssetInternalNode { BundleName = bundleName, AssetName = assetName };
                _nodes.Add(key, node);

                node.LoadingTask = LoadInternal(node);
            }

            node.RefCount++;
            await node.LoadingTask;

            var handle = new AssetHandle<T>(key, (h) => ReleaseNode(key));
            handle.Asset = node.TargetAsset as T;
            return handle;
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

    }

    internal class AssetBindingListener : MonoBehaviour
    {
        public IDisposable Handle;
        private void OnDestroy() => Handle?.Dispose();
    }
}