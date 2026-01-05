using UnityEditor;

namespace YY.Build
{
    /// <summary>
    /// 全局打包配置上下文
    /// </summary>
    public class BuildGraphContext
    {
        // 输出目录
        public string OutputPath;

        // 目标平台
        public BuildTarget TargetPlatform;

        public BuildTargetGroup TargetGroup;
        // 压缩格式 (默认 LZ4)
        public BuildAssetBundleOptions Options = BuildAssetBundleOptions.ChunkBasedCompression;

        // SBP 需要的一个参数，用于处理内置 Shader 等
        public UnityEditor.Build.Pipeline.Interfaces.IBundleBuildParameters BuildParameters;

        public BuildGraphContext(string outputPath, BuildTarget target)
        {
            OutputPath = outputPath;
            TargetPlatform = target;
        }
    }
}