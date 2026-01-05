using System;

namespace YY.Build
{
    /// <summary>
    /// 在节点间流转的最小数据单元
    /// </summary>
    [Serializable]
    public class AssetBuildInfo
    {
        // 资产在工程中的路径 (e.g., "Assets/Res/UI/Login.prefab")
        public string AssetPath;

        // 最终归属的 Bundle 名称 (e.g., "ui/login.bundle")
        // 如果为空，说明该资源还没被分配包名
        public string BundleName;

        // 可寻址名称 (LoadAsset 时用的名字，通常是文件名)
        public string AddressableName;

        public AssetBuildInfo(string path)
        {
            AssetPath = path;
            // 默认 Address 是文件名（不含后缀）
            AddressableName = System.IO.Path.GetFileNameWithoutExtension(path);
            BundleName = string.Empty;
        }
    }
}