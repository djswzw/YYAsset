using System;
using System.Threading.Tasks;

namespace YY
{
    public interface IAssetHandle : IDisposable
    {
        bool IsDone { get; }
        UnityEngine.Object RawAsset { get; }
    }

    public class AssetHandle<T> : IAssetHandle where T : UnityEngine.Object
    {
        public T Asset { get; internal set; }
        public UnityEngine.Object RawAsset => Asset;
        public bool IsDone => Asset != null;

        private string _key;
        private Action<AssetHandle<T>> _onRelease;

        internal AssetHandle(string key, Action<AssetHandle<T>> onRelease)
        {
            _key = key;
            _onRelease = onRelease;
        }

        public void Dispose()
        {
            _onRelease?.Invoke(this);
            _onRelease = null;
            Asset = null;
        }
    }

    public class AssetInternalNode
    {
        public UnityEngine.Object TargetAsset;
        public int RefCount;
        public string BundleName;
        public string AssetName;
        public Task LoadingTask;

        public void Release()
        {
            RefCount--;
            if (RefCount <= 0)
            {
                TargetAsset = null;
                BundleManager.UnloadBundle(BundleName);
            }
        }
    }
}