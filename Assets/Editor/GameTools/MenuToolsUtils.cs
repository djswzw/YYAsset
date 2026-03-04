using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEngine;
using YY;
using YY.Build.Core;

/// <summary>
/// AssetBundle编辑器菜单工具类
/// 提供模拟模式切换、平台路径配置、AssetBundle构建等功能
/// </summary>
public class MenuToolsUtils
{
    #region Constants - 菜单路径常量

    // Settings 分组
    private const string kSimulationMode = "GameTools/AssetBundles/Settings/Simulation Mode";
    private const string kLoadAndroidMode = "GameTools/AssetBundles/Settings/Android路径";
    private const string kLoadIosMode = "GameTools/AssetBundles/Settings/iOS路径";
    
    // Build 分组
    private const string kBuildForEditor = "GameTools/AssetBundles/Build/Build for Editor";
    private const string kBuildForStreamingAssets = "GameTools/AssetBundles/Build/Build for StreamingAssets";
    
    // 工具
    private const string kOpenBuildGraph = "GameTools/AssetBundles/Open Build Graph";

    #endregion

    #region Constants - EditorPrefs键名

    private const string kEditorPrefsBundleUrlKey = "bundleurl";
    private const string kBundleUrlAndroid = "android";
    private const string kBundleUrlIos = "ios";

    #endregion

    #region Constants - 默认构建配置

    /// <summary>
    /// 默认BuildGraph资产路径，可通过EditorPrefs自定义
    /// </summary>
    private const string kDefaultBuildGraphPath = "Assets/GameSource/main.asset";
    
    /// <summary>
    /// StreamingAssets相对路径（真机打包用）
    /// </summary>
    private const string kStreamingAssetsPath = "Assets/StreamingAssets/assets";

    #endregion

    #region Settings - 设置分组

    #region Simulation Mode - 模拟模式

    /// <summary>
    /// 切换模拟模式状态
    /// 启用时直接从AssetDatabase加载资源，无需打包
    /// </summary>
    [MenuItem(kSimulationMode)]
    public static void ToggleSimulationMode()
    {
        BundleManager.SimulateInEditor = !BundleManager.SimulateInEditor;
        Debug.Log($"[MenuTools] 模拟模式已{(BundleManager.SimulateInEditor ? "启用" : "禁用")}");
    }

    [MenuItem(kSimulationMode, true)]
    public static bool ToggleSimulationModeValidate()
    {
        Menu.SetChecked(kSimulationMode, BundleManager.SimulateInEditor);
        return true;
    }

    #endregion

    #region Platform Path - 平台路径配置

    /// <summary>
    /// 设置Android平台Bundle加载路径
    /// 编辑器模拟时从 StreamingRes/android/ 加载
    /// </summary>
    [MenuItem(kLoadAndroidMode)]
    public static void SetLoadPathAndroid()
    {
        ToggleBundleUrlPlatform(kBundleUrlAndroid);
    }

    [MenuItem(kLoadAndroidMode, true)]
    public static bool SetLoadPathAndroidValidate()
    {
        return ValidateBundleUrlPlatform(kLoadAndroidMode, kBundleUrlAndroid);
    }

    /// <summary>
    /// 设置iOS平台Bundle加载路径
    /// 编辑器模拟时从 StreamingRes/ios/ 加载
    /// </summary>
    [MenuItem(kLoadIosMode)]
    public static void SetLoadPathIos()
    {
        ToggleBundleUrlPlatform(kBundleUrlIos);
    }

    [MenuItem(kLoadIosMode, true)]
    public static bool SetLoadPathIosValidate()
    {
        return ValidateBundleUrlPlatform(kLoadIosMode, kBundleUrlIos);
    }

    private static void ToggleBundleUrlPlatform(string platformValue)
    {
        var currentUrl = EditorPrefs.GetString(kEditorPrefsBundleUrlKey, string.Empty);
        if (currentUrl == platformValue)
        {
            EditorPrefs.DeleteKey(kEditorPrefsBundleUrlKey);
            Debug.Log($"[MenuTools] 已清除平台路径配置");
        }
        else
        {
            EditorPrefs.SetString(kEditorPrefsBundleUrlKey, platformValue);
            Debug.Log($"[MenuTools] 平台路径已设置为: {platformValue}");
        }
    }

    private static bool ValidateBundleUrlPlatform(string menuPath, string platformValue)
    {
        var isCheck = EditorPrefs.GetString(kEditorPrefsBundleUrlKey, string.Empty) == platformValue;
        Menu.SetChecked(menuPath, isCheck);
        return true;
    }

    #endregion

    #endregion

    #region Build - 构建分组

    /// <summary>
    /// 构建AssetBundle并复制到编辑器模拟路径
    /// 目标路径: StreamingRes/{platform}/
    /// 用于编辑器内模拟测试
    /// </summary>
    [MenuItem(kBuildForEditor)]
    public static void BuildForEditor()
    {
        var graphPath = GetValidGraphPath();
        if (string.IsNullOrEmpty(graphPath)) return;

        Debug.Log($"[MenuTools] 开始构建 (Editor模式)，Graph路径: {graphPath}");
        
        // 执行构建并复制到编辑器模拟路径
        HeadlessBuilder.Build(graphPath, "Batch", copyToLoadPath: true, copyToStreamingAssets: false);
    }

    /// <summary>
    /// 构建AssetBundle并复制到StreamingAssets
    /// 目标路径: Assets/StreamingAssets/assets/
    /// 用于真机打包
    /// </summary>
    [MenuItem(kBuildForStreamingAssets)]
    public static void BuildForStreamingAssets()
    {
        var graphPath = GetValidGraphPath();
        if (string.IsNullOrEmpty(graphPath)) return;

        Debug.Log($"[MenuTools] 开始构建 (StreamingAssets模式)，Graph路径: {graphPath}");
        
        // 执行构建并复制到StreamingAssets
        HeadlessBuilder.Build(graphPath, "Batch", copyToLoadPath: false, copyToStreamingAssets: true);
    }

    /// <summary>
    /// 获取有效的Graph路径，如果无效则返回null
    /// </summary>
    private static string GetValidGraphPath()
    {
        var graphPath = EditorPrefs.GetString("BuildGraphPath", kDefaultBuildGraphPath);
        
        if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets", ""), graphPath)))
        {
            Debug.LogError($"[MenuTools] BuildGraph资产不存在: {graphPath}\n请先创建BuildGraph或设置正确的路径。");
            EditorUtility.DisplayDialog("构建失败", 
                $"BuildGraph资产不存在:\n{graphPath}\n\n请通过菜单 'GameTools/AssetBundles/Open Build Graph' 创建或配置Graph资产。", 
                "确定");
            return null;
        }

        return graphPath;
    }

    #endregion

    #region Tools - 工具分组

    /// <summary>
    /// 打开Build Graph编辑器窗口
    /// 用于创建、编辑和配置打包流程图
    /// </summary>
    [MenuItem(kOpenBuildGraph)]
    public static void OpenBuildGraphWindow()
    {
        var graphPath = EditorPrefs.GetString("BuildGraphPath", kDefaultBuildGraphPath);
        var graphAsset = AssetDatabase.LoadAssetAtPath<YY.Build.Data.BuildGraphAsset>(graphPath);
        
        if (graphAsset == null)
        {
            if (EditorUtility.DisplayDialog("Build Graph 不存在", 
                $"未找到BuildGraph资产: {graphPath}\n\n是否创建新的BuildGraph?", 
                "创建", "取消"))
            {
                graphAsset = ScriptableObject.CreateInstance<YY.Build.Data.BuildGraphAsset>();
                AssetDatabase.CreateAsset(graphAsset, kDefaultBuildGraphPath);
                AssetDatabase.SaveAssets();
                EditorPrefs.SetString("BuildGraphPath", kDefaultBuildGraphPath);
                Debug.Log($"[MenuTools] 已创建新的BuildGraph: {kDefaultBuildGraphPath}");
            }
            else
            {
                return;
            }
        }

        Selection.activeObject = graphAsset;
        EditorGUIUtility.PingObject(graphAsset);
        YY.Build.Graph.BuildGraphWindow.Open(graphAsset);
    }

    /// <summary>
    /// 设置BuildGraph资产路径（供外部调用）
    /// </summary>
    public static void SetBuildGraphPath(string graphPath)
    {
        if (string.IsNullOrEmpty(graphPath))
        {
            Debug.LogWarning("[MenuTools] BuildGraph路径不能为空");
            return;
        }
        
        EditorPrefs.SetString("BuildGraphPath", graphPath);
        Debug.Log($"[MenuTools] BuildGraph路径已设置为: {graphPath}");
    }

    #endregion
}
