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

        public System.Text.StringBuilder Logs = new System.Text.StringBuilder();

        public bool IsBuildMode = false;

        public List<BuildReportItem> Reports = new List<BuildReportItem>();
        public BuildContext() { }
    }
    public class BuildReportItem
    {
        public string NodeTitle;      // 节点名字 (e.g., "Build Android")
        public string Category;       // 类型 (Bundle, Zip, Copy)
        public string OutputPath;     // 输出到了哪里
        public int AssetCount;        // 处理了多少个资源
        public long OutputSizeBytes;  // 产出文件总大小 (字节)
        public double DurationSeconds;// 耗时
        public bool IsSuccess;        // 是否成功
        public string Message;        // 备注/错误信息
    }
}