using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YY
{
    public static class ExtensionMethods
    {
        // 1. 支持加载 Bundle 文件
        public static UnityAsyncAwaiter<AssetBundleCreateRequest, AssetBundle> GetAwaiter(this AssetBundleCreateRequest op)
        {
            return new UnityAsyncAwaiter<AssetBundleCreateRequest, AssetBundle>(op);
        }

        // 2. 支持从 Bundle 加载 Asset
        public static UnityAsyncAwaiter<AssetBundleRequest, UnityEngine.Object> GetAwaiter(this AssetBundleRequest op)
        {
            return new UnityAsyncAwaiter<AssetBundleRequest, UnityEngine.Object>(op);
        }

        // 3. 支持通用操作 (如场景加载 ResourceRequest)
        public static UnityAsyncAwaiter<AsyncOperation, AsyncOperation> GetAwaiter(this AsyncOperation op)
        {
            return new UnityAsyncAwaiter<AsyncOperation, AsyncOperation>(op);
        }
    }

    // 通用 Awaiter 结构体
    // TRequest: Unity 的异步操作类型
    // TResult: 我们希望 await 返回的结果类型
    public struct UnityAsyncAwaiter<TRequest, TResult> : INotifyCompletion where TRequest : AsyncOperation
    {
        private TRequest _op;

        public UnityAsyncAwaiter(TRequest op) { _op = op; }

        public bool IsCompleted => _op.isDone;

        public void OnCompleted(Action continuation) => _op.completed += _ => continuation();

        public TResult GetResult()
        {
            if (_op is AssetBundleCreateRequest bundleReq)
                return (TResult)(object)bundleReq.assetBundle;

            if (_op is AssetBundleRequest assetReq)
                return (TResult)(object)assetReq.asset;

            return (TResult)(object)_op;
        }
    }
}