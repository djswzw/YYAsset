using UnityEditor;
using UnityEngine;
using YY;

public class ApplicationLauncher : MonoBehaviour
{
    async void Start()
    {
        FileUtils.ResetPath();
        BundleManager.overrideBaseDownloadingURL = GetBundlePath;
        await BundleManager.InitializeAsync("android");
        await BundleManager.LoadSceneAsync("scenes/uiscene.b", "uiscene",true);
        AssetHandle<GameObject> uilogin = await AssetSystem.LoadAsync<GameObject>("ui/uiloginview.b", "UILoginView");
        var uiroot = GameObject.Find("UIRoot/main");
        Instantiate(uilogin.Asset, uiroot.transform);
    }

    public string GetBundlePath(string bundleName)
    {
        string path = FileUtils.NativePath;
        return path;
    }
}