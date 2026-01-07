using System.Collections.Generic;
using YY.Build.Data;

namespace YY.Build
{
    /// <summary>
    /// 在节点执行期间传递的上下文数据
    /// </summary>
    public class BuildContext
    {
        // 核心数据：当前流转的资产列表
        public List<AssetBuildInfo> Assets = new List<AssetBuildInfo>();

        // 可选：用于记录日志
        public System.Text.StringBuilder Logs = new System.Text.StringBuilder();

        public bool IsBuildMode = false;

        public BuildContext() { }
    }
}