
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YY
{
    public interface IBundlePathProvider
    {
        /// <summary>
        /// 获取 Bundle 的最终加载路径（沙盒 或 StreamingAssets）
        /// </summary>
        string GetBundlePath(string bundleName);

        /// <summary>
        /// 检查 Bundle 是否有效（基于配置表或物理文件）
        /// </summary>
        bool Exists(string bundleName);
    }
    public class DefaultBundlePathProvider : IBundlePathProvider
    {

        public string GetBundlePath(string bundleName)
        {
            string sandboxPath = FileUtils.PersistentPath + bundleName;
            if (File.Exists(sandboxPath))
            {
                return sandboxPath;
            }
            return FileUtils.NativePath + bundleName;
        }

        public bool Exists(string bundleName)
        {
            string sandboxPath = FileUtils.PersistentPath + bundleName;
            if (File.Exists(sandboxPath)) return true;
            //Todo 默认认为必然存在，额外逻辑根据具体实现判定
            return true;
        }
    }
}