using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Windows
{
    public class AssetListWindow : EditorWindow
    {
        private List<AssetBuildInfo> _allItems;
        private List<AssetBuildInfo> _filteredItems;
        private ListView _listView;
        private ToolbarSearchField _searchField;

        public static void Open(List<AssetBuildInfo> assets, string title)
        {
            var win = GetWindow<AssetListWindow>("Asset Viewer");
            win.titleContent = new GUIContent(string.IsNullOrEmpty(title) ? "Asset List" : title);
            win._allItems = new List<AssetBuildInfo>(assets);
            win._filteredItems = new List<AssetBuildInfo>(assets);
            win.Refresh();
            win.Show();
        }

        private void CreateGUI()
        {
            // 1. 顶部工具栏
            var toolbar = new Toolbar();

            _searchField = new ToolbarSearchField();
            _searchField.style.width = 250;
            _searchField.RegisterValueChangedCallback(evt => FilterList(evt.newValue));

            var countLabel = new Label("Count: 0") { name = "count-label" };
            countLabel.style.marginLeft = 10;
            countLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            toolbar.Add(_searchField);
            toolbar.Add(countLabel);
            rootVisualElement.Add(toolbar);

            // 2. 列表视图 (ListView)
            // 使用 makeItem 和 bindItem 来实现高性能渲染
            _listView = new ListView();
            _listView.style.flexGrow = 1;
            _listView.fixedItemHeight = 22;
            _listView.makeItem = () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 5 } };
                var bundleLabel = new Label { name = "bundle", style = { width = 150, color = new Color(0.4f, 0.8f, 1f) } };
                var pathLabel = new Label { name = "path", style = { flexGrow = 1 } };
                row.Add(bundleLabel);
                row.Add(pathLabel);
                return row;
            };

            _listView.bindItem = (element, index) =>
            {
                if (index >= _filteredItems.Count) return;
                var info = _filteredItems[index];

                var bundleLbl = element.Q<Label>("bundle");
                var pathLbl = element.Q<Label>("path");

                bundleLbl.text = string.IsNullOrEmpty(info.BundleName) ? "[-]" : info.BundleName;
                pathLbl.text = info.AssetPath;
            };

            // 设置数据源
            _listView.itemsSource = _filteredItems;

            // 双击 Ping 资源
            _listView.itemsChosen += (objs) =>
            {
                if (_listView.selectedIndex >= 0 && _listView.selectedIndex < _filteredItems.Count)
                {
                    var path = _filteredItems[_listView.selectedIndex].AssetPath;
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                    EditorGUIUtility.PingObject(obj);
                }
            };

            rootVisualElement.Add(_listView);
        }

        private void FilterList(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                _filteredItems = new List<AssetBuildInfo>(_allItems);
            }
            else
            {
                keyword = keyword.ToLower();
                _filteredItems = _allItems
                    .Where(a => a.AssetPath.ToLower().Contains(keyword) ||
                                (!string.IsNullOrEmpty(a.BundleName) && a.BundleName.ToLower().Contains(keyword)))
                    .ToList();
            }

            // 更新 UI
            _listView.itemsSource = _filteredItems;
            _listView.Rebuild();
            UpdateCount();
        }

        private void Refresh()
        {
            if (_listView != null)
            {
                _filteredItems = new List<AssetBuildInfo>(_allItems);
                _listView.itemsSource = _filteredItems;
                _listView.Rebuild();
                if (_searchField != null) _searchField.value = "";
                UpdateCount();
            }
        }

        private void UpdateCount()
        {
            var lbl = rootVisualElement.Q<Label>("count-label");
            if (lbl != null) lbl.text = $"Count: {_filteredItems.Count} / {_allItems.Count}";
        }
    }
}