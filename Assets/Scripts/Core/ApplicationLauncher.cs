using UnityEngine;
using YY;

public class ApplicationLauncher : MonoBehaviour
{
    async void Start()
    {
        // 1. 初始化路径
        FileUtils.ResetPath();

        // 2. 配置加载路径回调
        BundleManager.overrideBaseDownloadingURL = (bundleName) => FileUtils.NativePath;

        // 3. 初始化 BundleManager
        // 模拟模式下此步骤直接返回，真机模式下加载 Manifest
        await BundleManager.InitializeAsync("android");

        // 4. 加载场景
        // 假设 Bundle名为 "scenes/uiscene.b"，场景名为 "uiscene"
        await BundleManager.LoadSceneAsync("scenes/uiscene.b", "uiscene", true);

        // 5. 加载 UI 资源
        // 假设 Bundle名为 "ui/uiloginview.b"，资源名为 "UILoginView"
        AssetHandle<GameObject> uilogin = await AssetSystem.LoadAsync<GameObject>("ui/uiloginview.b", "UILoginView");

        if (uilogin != null)
        {
            // 6. 实例化 UI
            var uiroot = GameObject.Find("UIRoot/main");
            if (uiroot != null)
            {
                GameObject view = Instantiate(uilogin.Asset, uiroot.transform);
                Debug.Log($"Load Success: {view.name}");

                // 简单的组件检查
                var script = view.GetComponent<UILoginView>();
                if (script != null && script.atlas != null)
                {
                    Debug.Log("Script and Atlas reference are valid.");
                }
            }
        }
        else
        {
            Debug.LogError("Failed to load UILoginView.");
        }
    }
}