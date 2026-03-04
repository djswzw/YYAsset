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

    private const string kSimulationMode = "GameTools/AssetBundles/Simulation Mode";
    private const string kLoadAndroidMode = "GameTools/AssetBundles/Android路径";
    private const string kLoadIosMode = "GameTools/AssetBundles/iOS路径";
    private const string kBuildAsset = "GameTools/AssetBundles/Build";
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
    private const string kDefaultBuildNodeName = "Build";

    #endregion

    #region Simulation Mode - 模拟模式

    /// <summary>
    /// 切换模拟模式状态
    /// </summary>
    [MenuItem(kSimulationMode)]
    public static void ToggleSimulationMode()
    {
        BundleManager.SimulateInEditor = !BundleManager.SimulateInEditor;
        Debug.Log($"[MenuTools] 模拟模式已{(BundleManager.SimulateInEditor ? "启用" : "禁用")}");
    }

    /// <summary>
    /// 验证模拟模式菜单项，并更新选中状态
    /// </summary>
    /// <returns>始终返回true，允许菜单项可点击</returns>
    [MenuItem(kSimulationMode, true)]
    public static bool ToggleSimulationModeValidate()
    {
        // 更新菜单项的选中状态（勾选标记）
        Menu.SetChecked(kSimulationMode, BundleManager.SimulateInEditor);
        return true;
    }

    #endregion

    #region Platform Path - 平台路径配置

    /// <summary>
    /// 设置Android平台Bundle路径
    /// </summary>
    [MenuItem(kLoadAndroidMode)]
    public static void SetSimulationModeAndroid()
    {
        ToggleBundleUrlPlatform(kBundleUrlAndroid);
    }

    /// <summary>
    /// 验证Android平台菜单项
    /// </summary>
    [MenuItem(kLoadAndroidMode, true)]
    public static bool SetSimulationModeAndroidValidate()
    {
        return ValidateBundleUrlPlatform(kLoadAndroidMode, kBundleUrlAndroid);
    }

    /// <summary>
    /// 设置iOS平台Bundle路径
    /// </summary>
    [MenuItem(kLoadIosMode)]
    public static void SetSimulationModeIos()
    {
        ToggleBundleUrlPlatform(kBundleUrlIos);
    }

    /// <summary>
    /// 验证iOS平台菜单项
    /// </summary>
    [MenuItem(kLoadIosMode, true)]
    public static bool SetSimulationModeIosValidate()
    {
        return ValidateBundleUrlPlatform(kLoadIosMode, kBundleUrlIos);
    }

    /// <summary>
    /// 切换指定平台的Bundle URL配置
    /// 如果当前已是该平台，则清除配置；否则设置为该平台
    /// </summary>
    /// <param name="platformValue">平台标识值（android/ios）</param>
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

    /// <summary>
    /// 验证平台路径菜单项，更新选中状态
    /// </summary>
    /// <param name="menuPath">菜单路径</param>
    /// <param name="platformValue">平台标识值</param>
    /// <returns>始终返回true</returns>
    private static bool ValidateBundleUrlPlatform(string menuPath, string platformValue)
    {
        var isCheck = EditorPrefs.GetString(kEditorPrefsBundleUrlKey, string.Empty) == platformValue;
        Menu.SetChecked(menuPath, isCheck);
        return true;
    }

    #endregion

    #region Build AssetBundles - 构建AssetBundle

    /// <summary>
    /// 使用图形打包工具构建AssetBundle
    /// 自动查找BatchBuildNode作为入口执行全图打包
    /// </summary>
    [MenuItem(kBuildAsset)]
    public static void BuildAsset()
    {
        // 从EditorPrefs获取配置的Graph路径，若无则使用默认值
        var graphPath = EditorPrefs.GetString("BuildGraphPath", kDefaultBuildGraphPath);
        
        // 检查Graph资产是否存在
        if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets", ""), graphPath)))
        {
            Debug.LogError($"[MenuTools] BuildGraph资产不存在: {graphPath}\n请先创建BuildGraph或设置正确的路径。");
            EditorUtility.DisplayDialog("构建失败", 
                $"BuildGraph资产不存在:\n{graphPath}\n\n请通过菜单 'GameTools/AssetBundles/Open Build Graph' 创建或配置Graph资产。", 
                "确定");
            return;
        }

        Debug.Log($"[MenuTools] 开始图形打包，Graph路径: {graphPath}");
        
        // 调用HeadlessBuilder执行无头构建（默认查找BatchBuildNode）
        HeadlessBuilder.Build(graphPath);
    }

    /// <summary>
    /// 打开Build Graph编辑器窗口
    /// 用于创建、编辑和配置打包流程图
    /// </summary>
    [MenuItem(kOpenBuildGraph)]
    public static void OpenBuildGraphWindow()
    {
        // 尝试加载现有的BuildGraph资产
        var graphPath = EditorPrefs.GetString("BuildGraphPath", kDefaultBuildGraphPath);
        var graphAsset = AssetDatabase.LoadAssetAtPath<YY.Build.Data.BuildGraphAsset>(graphPath);
        
        if (graphAsset == null)
        {
            // 如果不存在，提示用户创建
            if (EditorUtility.DisplayDialog("Build Graph 不存在", 
                $"未找到BuildGraph资产: {graphPath}\n\n是否创建新的BuildGraph?", 
                "创建", "取消"))
            {
                // 创建新的BuildGraph资产
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

        // 选中资产并在Inspector中显示
        Selection.activeObject = graphAsset;
        EditorGUIUtility.PingObject(graphAsset);
        
        // 打开BuildGraph窗口
        YY.Build.Graph.BuildGraphWindow.Open(graphAsset);
    }

    /// <summary>
    /// 设置BuildGraph资产路径（供外部调用）
    /// </summary>
    /// <param name="graphPath">BuildGraph资产的相对路径（如 Assets/NewBuildGraph.asset）</param>
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
