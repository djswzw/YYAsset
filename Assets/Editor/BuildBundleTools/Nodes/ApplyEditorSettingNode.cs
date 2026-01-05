using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class ApplyToEditorNode : BaseBuildNode
    {
        public override void Initialize()
        {
            title = "Editor: Set Bundle Names";

            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            var container = new VisualElement();
            container.style.paddingTop = 10;
            container.style.paddingBottom = 10;
            container.style.width = 200;

            var descLabel = new Label("Apply bundle names to \nAssetImporter for \nEditor Simulation.");
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.color = Color.gray;

            // 核心按钮
            var applyBtn = new Button(OnApplyClick)
            {
                text = "Apply to AssetDatabase",
                style = { height = 30, marginTop = 10 }
            };

            // 清理按钮
            var clearBtn = new Button(OnClearClick)
            {
                text = "Clear All Bundle Names",
                style = { height = 20, marginTop = 5, backgroundColor = new Color(0.6f, 0.2f, 0.2f) }
            };

            container.Add(descLabel);
            container.Add(applyBtn);
            container.Add(clearBtn);

            mainContainer.Add(container);
        }

        private void OnApplyClick()
        {
            if (!EditorUtility.DisplayDialog("Confirm", "This will modify the 'AssetBundle Name' of source assets in the Inspector. Continue?", "Yes", "No"))
                return;

            // 1. 运行图逻辑，收集数据
            Debug.Log("[ApplyToEditor] Collecting Assets...");
            var context = GraphRunner.Run(this);

            int count = 0;
            int changed = 0;

            try
            {
                AssetDatabase.StartAssetEditing(); // 开始批量编辑，提升性能

                foreach (var asset in context.Assets)
                {
                    if (string.IsNullOrEmpty(asset.BundleName)) continue;

                    var importer = AssetImporter.GetAtPath(asset.AssetPath);
                    if (importer != null)
                    {
                        // 只有当名字不一致时才修改，避免不必要的 Reimport
                        if (importer.assetBundleName != asset.BundleName)
                        {
                            importer.assetBundleName = asset.BundleName;
                            changed++;
                        }
                        count++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing(); // 结束批量编辑
            }

            // 移除未使用的 Bundle Name (可选，保持工程干净)
            AssetDatabase.RemoveUnusedAssetBundleNames();

            Debug.Log($"[ApplyToEditor] Processed {count} assets. Changed {changed} importers.");
            EditorUtility.DisplayDialog("Complete", $"Applied Bundle Names to {changed} assets.", "OK");
        }

        private void OnClearClick()
        {
            if (!EditorUtility.DisplayDialog("Warning", "This will clear ALL AssetBundle names in the entire project. Are you sure?", "Yes", "Cancel"))
                return;

            var names = AssetDatabase.GetAllAssetBundleNames();
            AssetDatabase.StartAssetEditing();
            foreach (var name in names)
            {
                AssetDatabase.RemoveAssetBundleName(name, true); // true = force remove
            }
            AssetDatabase.StopAssetEditing();

            Debug.Log("[ApplyToEditor] Cleared all AssetBundle names.");
        }

        // 此节点是终点，只记录日志
        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[ApplyToEditor] Ready to set BundleNames for {context.Assets.Count} assets.");
            return base.Execute(context);
        }

        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }
}