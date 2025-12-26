using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YY
{
    public class BundleInfo
    {
        public AssetBundle Bundle;
        public string Name;
        public int RefCount;

        public BundleInfo(string name, AssetBundle bundle)
        {
            Name = name;
            Bundle = bundle;
            RefCount = 0;
        }

        public void Retain() => RefCount++;
        public bool Release() => --RefCount <= 0;
    }

    public interface IBundleLoadStrategy
    {
        Task<AssetBundle> Load(string path);
    }

    public class LocalFileLoadStrategy : IBundleLoadStrategy
    {
        public async Task<AssetBundle> Load(string path)
        {
            var req = await AssetBundle.LoadFromFileAsync(path);
            return req;
        }
    }

    public class RequestScheduler
    {
        private int _maxConcurrency;
        private int _currentRunning;
        private Queue<TaskCompletionSource<bool>> _queue = new Queue<TaskCompletionSource<bool>>();

        public RequestScheduler(int max) => _maxConcurrency = max;

        public async Task WaitSlot()
        {
            if (_currentRunning < _maxConcurrency)
            {
                _currentRunning++;
                return;
            }
            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(tcs);
            await tcs.Task;
        }

        public void ReleaseSlot()
        {
            if (_queue.Count > 0)
                _queue.Dequeue().SetResult(true);
            else
                _currentRunning--;
        }
    }
}