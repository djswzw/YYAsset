using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;

namespace YY.Build.Graph.Nodes
{
    public class BuildZipNode : BaseBuildNode
    {
        // 配置数据
        public string OutputPath = "StreamingRes/Scripts";
        public string ZipFileName = "scripts.zip";
        public string Password = ""; // 空代表不加密

        // UI 引用
        private TextField _pathField;
        private TextField _nameField;
        private TextField _passField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Export: Build Zip";
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            AddOutputPort("Pass", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            // 样式：紫色背景区分
            titleContainer.style.backgroundColor = new Color(0.4f, 0.2f, 0.5f);

            var root = new VisualElement { style = { backgroundColor = new Color(0.2f, 0.2f, 0.2f) } };

            // 1. 输出路径
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _pathField = new TextField("Output Dir:") { value = OutputPath, style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(e => { OutputPath = e.newValue; NotifyChange(); });

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Output", OutputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath)) path = "Assets" + path.Substring(Application.dataPath.Length);
                    OutputPath = path;
                    _pathField.value = path;
                    NotifyChange();
                }
            })
            { text = "..." };
            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);

            // 2. 文件名
            _nameField = new TextField("Zip Name:") { value = ZipFileName };
            _nameField.RegisterValueChangedCallback(e => { ZipFileName = e.newValue; NotifyChange(); });

            // 3. 密码 (简单的混淆)
            _passField = new TextField("Password (XOR):") { value = Password, isPasswordField = true };
            _passField.RegisterValueChangedCallback(e => { Password = e.newValue; NotifyChange(); });

            // 4. 按钮
            var buildBtn = new Button(OnBuildClick)
            {
                text = "BUILD ZIP",
                style = { height = 30, marginTop = 10, backgroundColor = new Color(0.5f, 0.3f, 0.6f), unityFontStyleAndWeight = FontStyle.Bold }
            };

            root.Add(pathRow);
            root.Add(_nameField);
            root.Add(_passField);
            root.Add(buildBtn);

            mainContainer.Add(root);
        }

        private void OnBuildClick()
        {
            Debug.Log($"[BuildZipNode] Collecting Assets...");

            // 获取图里的所有节点 (支持 Portal)
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));

            // 执行
            GraphRunner.Run(this, allNodes, true);
        }

        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            if (context.IsBuildMode)
            {
                context.Logs.AppendLine($"[BuildZipNode] Zipping...");
                bool success = ZipBuilder.CreateZip(OutputPath, ZipFileName, context.Assets, Password);
                if (success) context.Logs.AppendLine("  Zip Success!");
            }
            else
            {
                context.Logs.AppendLine($"[BuildZipNode] Ready to Zip. (Preview Mode)");
            }
            return new System.Collections.Generic.Dictionary<string, BuildContext> { { "Pass", context } };
        }

        // 序列化
        [System.Serializable] class NodeData { public string outPath; public string zipName; public string pass; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { outPath = OutputPath, zipName = ZipFileName, pass = Password });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                OutputPath = data.outPath; ZipFileName = data.zipName; Password = data.pass;
                if (_pathField != null) _pathField.value = OutputPath;
                if (_nameField != null) _nameField.value = ZipFileName;
                if (_passField != null) _passField.value = Password;
            }
        }
    }
}