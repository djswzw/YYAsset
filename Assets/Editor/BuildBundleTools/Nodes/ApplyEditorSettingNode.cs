using System.Collections.Generic;
using System.Linq;
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
            base.Initialize();
            title = "Editor: Set Bundle Names";

            // 允许连接多个上游节点
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            var container = new VisualElement();
            container.style.paddingTop = 10;
            container.style.paddingBottom = 10;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            container.style.width = 220;
            container.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);

            var descLabel = new Label("Sets 'AssetBundle Name' in Inspector.\n(Required for Simulation Mode)");
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            descLabel.style.marginBottom = 10;

            // 核心按钮
            var applyBtn = new Button(OnApplyClick)
            {
                text = "Apply to AssetDatabase",
                style = { height = 30, backgroundColor = new Color(0.2f, 0.5f, 0.2f), unityFontStyleAndWeight = FontStyle.Bold }
            };

            // 清理按钮
            var clearBtn = new Button(OnClearClick)
            {
                text = "Clear All Project Bundle Names",
                style = { height = 20, marginTop = 10, backgroundColor = new Color(0.6f, 0.2f, 0.2f) }
            };

            container.Add(descLabel);
            container.Add(applyBtn);
            container.Add(clearBtn);

            mainContainer.Add(container);
        }

        private void OnApplyClick()
        {
            //if (!EditorUtility.DisplayDialog("Confirm", "This will modify the 'AssetBundle Name' settings in the Inspector for ALL collected assets. Continue?", "Yes", "No"))
            //    return;

            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = graphView.nodes.ToList().Cast<BaseBuildNode>().ToList();

            var context = GraphRunner.Run(this, allNodes);

            if (context.Assets == null || context.Assets.Count == 0)
            {
                Debug.LogWarning("[ApplyToEditor] 0 Assets collected. Please check upstream connections.");
                return;
            }

            int totalCount = context.Assets.Count;
            int validBundleCount = 0;
            int changedCount = 0;

            try
            {
                // 开始批量编辑，提高性能
                AssetDatabase.StartAssetEditing();

                foreach (var asset in context.Assets)
                {
                    // 1. 检查 BundleName 是否为空
                    if (string.IsNullOrEmpty(asset.BundleName))
                    {
                        // 如果你直接连 DirectoryNode，这里会为空，这是正常的，但也意味着不会应用设置
                        continue;
                    }

                    validBundleCount++;

                    // 2. 获取 Importer
                    var importer = AssetImporter.GetAtPath(asset.AssetPath);
                    if (importer == null)
                    {
                        Debug.LogError($"[ApplyToEditor] Could not find AssetImporter for: {asset.AssetPath}");
                        continue;
                    }

                    // 3. 应用设置 (仅当变化时才赋值，避免不必要的 Reimport)
                    if (importer.assetBundleName != asset.BundleName)
                    {
                        importer.assetBundleName = asset.BundleName;
                        changedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // 清理未使用的包名，保持列表干净
            AssetDatabase.RemoveUnusedAssetBundleNames();
            AssetDatabase.SaveAssets(); // 强制写入磁盘

            Debug.Log($"[ApplyToEditor] Result:\n" +
                      $"- Total Assets: {totalCount}\n" +
                      $"- Valid BundleNames: {validBundleCount}\n" +
                      $"- Actually Changed: {changedCount}");

            if (validBundleCount == 0)
            {
                EditorUtility.DisplayDialog("Warning", "Assets received but NONE had a BundleName set.\nDid you forget to connect a GrouperNode?", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Complete", $"Applied settings to {changedCount} assets.", "OK");
            }
        }

        private void OnClearClick()
        {
            if (!EditorUtility.DisplayDialog("Warning", "This will clear ALL AssetBundle names in the entire project.\nAre you sure?", "Yes", "Cancel"))
                return;

            var names = AssetDatabase.GetAllAssetBundleNames();
            int count = names.Length;

            AssetDatabase.StartAssetEditing();
            foreach (var name in names)
            {
                AssetDatabase.RemoveAssetBundleName(name, true);
            }
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();

            Debug.Log($"[ApplyToEditor] Cleared {count} bundle names.");
        }

        // 必须实现序列化，否则 Undo/Load 会出错
        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }

        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 记录一下日志供 Preview 查看
            context.Logs.AppendLine($"[ApplyToEditor] Node Ready. (Click button to apply)");
            return base.Execute(context);
        }
    }
}