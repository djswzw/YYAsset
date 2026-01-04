using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using YY;

public class ResourceDebuggerWindow : EditorWindow
{
    [MenuItem("GameTools/Resource Debugger")]
    public static void OpenWindow()
    {
        GetWindow<ResourceDebuggerWindow>("Res Debugger").Show();
    }

    private BundleInfo _selectedBundle;
    private Vector2 _scrollPosLeft;
    private Vector2 _scrollPosRight;
    private string _searchFilter = "";

    // 自动刷新，保证数据实时性
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        DrawToolbar();

        // 左右分栏布局
        EditorGUILayout.BeginHorizontal();
        {
            // 左侧：Bundle 列表 (占窗口 40%)
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));
            DrawBundleListPanel();
            EditorGUILayout.EndVertical();

            // 分割线
            GUILayout.Box("", GUILayout.Width(1), GUILayout.ExpandHeight(true));

            // 右侧：依赖详情 (占窗口 60%)
            EditorGUILayout.BeginVertical();
            DrawBundleDetailPanel();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Bundle Filter:", GUILayout.Width(80));
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarTextField);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Export Log", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            ExportSnapshot();
        }

        if (GUILayout.Button("Force GC", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }
        EditorGUILayout.EndHorizontal();
    }

    // --- 左侧：Bundle 列表 ---
    private void DrawBundleListPanel()
    {
        DrawHeader("Loaded Bundles");
        _scrollPosLeft = EditorGUILayout.BeginScrollView(_scrollPosLeft);

        var bundles = BundleManager.GetLoadedBundleInfos();

        // 排序：引用计数 > 0 的排前面，然后按名字排
        var sortedBundles = bundles.OrderByDescending(b => b.RefCount > 0).ThenBy(b => b.Name);

        GUIStyle listBtnStyle = new GUIStyle("CN EntryBackEven"); // 使用 Unity 内置的列表背景样式
        listBtnStyle.alignment = TextAnchor.MiddleLeft;
        listBtnStyle.padding = new RectOffset(10, 0, 0, 0); // 左边留点空隙
        listBtnStyle.fixedHeight = 25; // 固定高度，方便点击

        foreach (var bundle in sortedBundles)
        {
            if (!string.IsNullOrEmpty(_searchFilter) && !bundle.Name.Contains(_searchFilter)) continue;

            // 绘制选中高亮背景
            if (_selectedBundle != null && _selectedBundle.Name == bundle.Name)
            {
                GUI.backgroundColor = Color.cyan;
            }
            else
            {
                GUI.backgroundColor = bundle.RefCount > 0 ? new Color(0.6f, 1f, 0.6f) : Color.white;
            }

            if (GUILayout.Button($"{bundle.Name} ({bundle.RefCount})", listBtnStyle, GUILayout.ExpandWidth(true)))
            {
                _selectedBundle = bundle;
                GUI.FocusControl(null);
            }

            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndScrollView();
    }

    private void ExportSnapshot()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Memory Snapshot - {System.DateTime.Now}");
        sb.AppendLine("========================================");

        var bundles = BundleManager.GetLoadedBundleInfos();

        // 统计总数
        sb.AppendLine($"Total Loaded Bundles: {bundles.Count}");
        sb.AppendLine($"Total Active Assets: {AssetSystem.GetLoadedAssetNodes().Count}");
        sb.AppendLine("========================================");
        sb.AppendLine("");

        foreach (var bundle in bundles)
        {
            sb.AppendLine($"[Bundle] {bundle.Name}");
            sb.AppendLine($"  RefCount: {bundle.RefCount}");

            var parents = BundleManager.GetLoadedBundlesThatDependOn(bundle.Name);
            var assets = AssetSystem.GetLoadedAssetsInBundle(bundle.Name);

            if (parents.Count > 0)
            {
                sb.AppendLine("  Referenced By Bundles:");
                foreach (var p in parents) sb.AppendLine($"    - {p}");
            }

            if (assets.Count > 0)
            {
                sb.AppendLine("  Referenced By Assets:");
                foreach (var a in assets) sb.AppendLine($"    - {a}");
            }

            sb.AppendLine("");
        }

        string path = Path.Combine(Application.dataPath, "..", "BundleMemoryDump.txt");
        File.WriteAllText(path, sb.ToString());

        // 自动打开文件
        EditorUtility.RevealInFinder(path);
        Application.OpenURL(path);
        Debug.Log($"Snapshot exported to: {path}");
    }

    // --- 右侧：依赖详情 ---
    private void DrawBundleDetailPanel()
    {
        DrawHeader("Dependency Inspector");

        if (_selectedBundle == null)
        {
            GUILayout.Label("Select a bundle from the list to view details.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        _scrollPosRight = EditorGUILayout.BeginScrollView(_scrollPosRight);

        // 1. 基本信息
        EditorGUILayout.LabelField("Name", _selectedBundle.Name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Ref Count", _selectedBundle.RefCount.ToString(), EditorStyles.boldLabel);

        EditorGUILayout.Space(10);

        // 2. 谁在引用我？ (Upstream / Referencers)
        // 引用来源只有两类：A. 该 Bundle 里的 Asset; B. 依赖该 Bundle 的其他 Bundle
        DrawSectionHeader("Referenced By (谁在引用我?)");

        // A. 查 Asset
        var assetsInBundle = AssetSystem.GetLoadedAssetsInBundle(_selectedBundle.Name);
        if (assetsInBundle.Count > 0)
        {
            GUILayout.Label($"[Assets] ({assetsInBundle.Count})", EditorStyles.miniBoldLabel);
            foreach (var assetName in assetsInBundle)
            {
                DrawItem($"Asset: {assetName}");
            }
        }

        // B. 查 Bundle (反向查找)
        var parentBundles = BundleManager.GetLoadedBundlesThatDependOn(_selectedBundle.Name);
        if (parentBundles.Count > 0)
        {
            GUILayout.Label($"[Parent Bundles] ({parentBundles.Count})", EditorStyles.miniBoldLabel);
            foreach (var parent in parentBundles)
            {
                DrawItem($"Bundle: {parent}");
            }
        }

        if (assetsInBundle.Count == 0 && parentBundles.Count == 0)
        {
            if (_selectedBundle.RefCount > 0)
                GUILayout.Label("Ref Count > 0 未追踪到持有者记录.", EditorStyles.helpBox);
            else
                GUILayout.Label("无引用，可以安全卸载", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(10);

        // 3. 我依赖了谁？
        DrawSectionHeader("Dependencies (我依赖了谁?)");

        var manifest = BundleManager.GetManifest();
        if (manifest != null)
        {
            string[] deps = manifest.GetAllDependencies(_selectedBundle.Name);
            if (deps.Length > 0)
            {
                foreach (var dep in deps)
                {
                    DrawItem($"Bundle: {dep}");
                }
            }
            else
            {
                GUILayout.Label("No Dependencies", EditorStyles.miniLabel);
            }
        }
        else
        {
            GUILayout.Label("Manifest not loaded (Simulation Mode?)", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(10);

        if (_selectedBundle.Bundle != null)
        {
            DrawSectionHeader("Contains Assets");
            var assetNames = _selectedBundle.Bundle.GetAllAssetNames();
            foreach (var name in assetNames)
            {
                GUILayout.Label(System.IO.Path.GetFileName(name), EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader(string title)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(title, EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSectionHeader(string title)
    {
        var style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = new Color(0.3f, 0.6f, 0.9f);
        GUILayout.Label(title, style);
        GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
    }

    private void DrawItem(string label)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("↳", GUILayout.Width(15));
        GUILayout.Label(label);
        EditorGUILayout.EndHorizontal();
    }
}