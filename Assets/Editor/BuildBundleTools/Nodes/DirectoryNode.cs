using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    public class DirectoryNode : BaseBuildNode
    {
        public string FolderPath = "Assets";
        private TextField _textField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Source: Directory";
            AddOutputPort("Output", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            _textField = new TextField { value = FolderPath, style = { flexGrow = 1 } };
            _textField.RegisterValueChangedCallback(evt => FolderPath = evt.newValue);

            _textField.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });
            _textField.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                if (DragAndDrop.paths.Length > 0)
                {
                    string path = DragAndDrop.paths[0];
                    if (Directory.Exists(path)) // 确保是文件夹
                    {
                        // 确保是相对路径
                        if (path.StartsWith(Application.dataPath))
                            path = "Assets" + path.Substring(Application.dataPath.Length);

                        FolderPath = path;
                        _textField.value = path;
                        NotifyChange();
                    }
                }
            });
            _textField.tooltip = "Drag a folder here to scan.";

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    int index = path.IndexOf("Assets");
                    if (index >= 0)
                    {
                        path = path.Substring(index);
                        FolderPath = path;
                        _textField.value = path;
                        NotifyChange();
                    }
                }
            })
            { text = "..." };

            container.Add(_textField);
            container.Add(browseBtn);
            mainContainer.Add(container);
        }

        // ... (Save/Load 和 Execute 保持不变) ...
        [System.Serializable] private class NodeData { public string path; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { path = FolderPath });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { FolderPath = data.path; if (_textField != null) _textField.value = FolderPath; }
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[DirectoryNode] Scanning: {FolderPath}");
            // ... (保持原有的扫描逻辑)
            if (!Directory.Exists(FolderPath)) return new Dictionary<string, BuildContext> { { "Output", context } };

            string[] files = Directory.GetFiles(FolderPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.EndsWith(".meta") || file.EndsWith(".cs")) continue;
                context.Assets.Add(new AssetBuildInfo(file.Replace("\\", "/")));
            }
            context.Logs.AppendLine($"  Found {context.Assets.Count} assets.");
            return new Dictionary<string, BuildContext> { { "Output", context } };
        }
    }
}