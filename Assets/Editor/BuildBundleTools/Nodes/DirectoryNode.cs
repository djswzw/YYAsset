using System.Collections.Generic; // 必须引用，用于 Dictionary
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data; // 引用 BuildContext

namespace YY.Build.Graph.Nodes
{
    public class DirectoryNode : BaseBuildNode
    {
        public string FolderPath = "Assets";
        private TextField _textField; // 持有 UI 引用，用于 Load 时刷新显示

        public override void Initialize()
        {
            title = "Source: Directory";

            // 添加标准输出端口
            AddOutputPort("Output");

            // --- UI 构建 ---
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            _textField = new TextField { value = FolderPath, style = { flexGrow = 1 } };
            _textField.RegisterValueChangedCallback(evt => FolderPath = evt.newValue);

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // 转换为工程相对路径
                    int index = path.IndexOf("Assets");
                    if (index >= 0)
                    {
                        path = path.Substring(index);
                        FolderPath = path;
                        _textField.value = path; // 刷新 UI
                    }
                    else
                    {
                        Debug.LogError("请选择工程 Assets 目录下的文件夹！");
                    }
                }
            })
            { text = "..." };

            browseBtn.style.width = 30;

            container.Add(_textField);
            container.Add(browseBtn);
            mainContainer.Add(container);
        }

        // --- 序列化逻辑 (保持不变) ---
        [System.Serializable]
        private class NodeData
        {
            public string path;
        }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData { path = FolderPath });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                FolderPath = data.path;
                if (_textField != null) _textField.value = FolderPath;
            }
        }

        // --- 执行逻辑 (升级适配新架构) ---
        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 目录节点通常作为源头，context 参数可能是空的，我们在内部填充它
            // 如果上游有节点传来数据，我们选择保留还是覆盖？通常 DirectoryNode 是起点，追加数据比较合理。

            context.Logs.AppendLine($"[DirectoryNode] Scanning: {FolderPath}");

            if (!Directory.Exists(FolderPath))
            {
                context.Logs.AppendLine($"  Error: Directory not found: {FolderPath}");
                // 返回空结果，但也必须包含 Output 键
                return new Dictionary<string, BuildContext> { { "Output", context } };
            }

            string[] files = Directory.GetFiles(FolderPath, "*", SearchOption.AllDirectories);
            int count = 0;
            foreach (var file in files)
            {
                // 忽略 .meta 和 .cs 文件
                if (file.EndsWith(".meta") || file.EndsWith(".cs")) continue;

                string unityPath = file.Replace("\\", "/");

                // 添加到资产列表
                context.Assets.Add(new AssetBuildInfo(unityPath));
                count++;
            }

            context.Logs.AppendLine($"  Found {count} assets.");

            // 返回标准字典：Key 必须与 AddOutputPort("Output") 的名字一致
            return new Dictionary<string, BuildContext> { { "Output", context } };
        }
    }
}