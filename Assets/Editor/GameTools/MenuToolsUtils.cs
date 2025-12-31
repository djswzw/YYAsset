using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEngine;
using YY;
public class MenuToolsUtils
{
    const string kSimulationMode = "GameTools/AssetBundles/Simulation Mode";

    [MenuItem(kSimulationMode)]
    public static void ToggleSimulationMode()
    {
        BundleManager.SimulateInEditor = !BundleManager.SimulateInEditor;
    }

    [MenuItem(kSimulationMode, true)]
    public static bool ToggleSimulationModeValidate()
    {
        Menu.SetChecked(kSimulationMode, BundleManager.SimulateInEditor);
        return true;
    }

    const string kLoadAndroidMode = "GameTools/AssetBundles/加载android路径";
    [MenuItem(kLoadAndroidMode)]
    public static void SeteSimulationModeAndroid()
    {
        var isCheck = EditorPrefs.GetString("bundleurl") == "android";
        if (isCheck)
        {
            EditorPrefs.DeleteKey("bundleurl");
        }
        else
        {
            EditorPrefs.SetString("bundleurl", "android");
        }
    }

    [MenuItem(kLoadAndroidMode, true)]
    public static bool SeteSimulationModeAndroidValidate()
    {
        var isCheck = EditorPrefs.GetString("bundleurl") == "android";
        Menu.SetChecked(kLoadAndroidMode, isCheck);

        return true;
    }

    const string kLoadIosMode = "GameTools/AssetBundles/加载ios路径";
    [MenuItem(kLoadIosMode)]
    public static void SeteSimulationModeios()
    {
        var isCheck = EditorPrefs.GetString("bundleurl") == "ios";
        if (isCheck)
        {
            EditorPrefs.DeleteKey("bundleurl");
        }
        else
        {
            EditorPrefs.SetString("bundleurl", "ios");
        }
    }


    [MenuItem(kLoadIosMode, true)]
    public static bool SeteSimulationModeiosValidate()
    {
        var isCheck = EditorPrefs.GetString("bundleurl") == "ios";
        Menu.SetChecked(kLoadIosMode, isCheck);
        return true;
    }
    [MenuItem("GameTools/AssetBundles/Build")]
    public static void BuildAsset()
    {
        OldBuildAssetBundles("StreamingRes/android", true, true, BuildTarget.StandaloneWindows64);
        //BuildAssetBundles("StreamingRes/android", true, true, BuildTarget.StandaloneWindows64);
    }

    public static bool OldBuildAssetBundles(string outputPath, bool forceRebuild, bool useChunkBasedCompression, BuildTarget buildTarget)
    {
        var options = BuildAssetBundleOptions.None;
        if (useChunkBasedCompression)
            options |= BuildAssetBundleOptions.ChunkBasedCompression;

        if (forceRebuild)
            options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

        Directory.CreateDirectory(outputPath);
        var manifest = BuildPipeline.BuildAssetBundles(outputPath, options, buildTarget);
        return manifest != null;
    }

    public static bool BuildAssetBundles(string outputPath, bool forceRebuild, bool useChunkBasedCompression, BuildTarget buildTarget)
    {
        var options = BuildAssetBundleOptions.None;
        if (useChunkBasedCompression)
            options |= BuildAssetBundleOptions.ChunkBasedCompression;

        if (forceRebuild)
            options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

        var bundles = ContentBuildInterface.GenerateAssetBundleBuilds();
        for (var i = 0; i < bundles.Length; i++)
        {
            var strs = bundles[i].assetNames.Select(Path.GetFileNameWithoutExtension).ToArray();
            foreach(var s in strs)
            {
                Debug.Log(s);
            }
            bundles[i].addressableNames = strs;
            
        }
        Directory.CreateDirectory(outputPath);
        var manifest = CompatibilityBuildPipeline.BuildAssetBundles(outputPath, bundles,options, buildTarget);
        return manifest != null;
    }
}
