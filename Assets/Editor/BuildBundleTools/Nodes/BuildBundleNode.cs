using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;
using YY.Build.Data; // 引用 BuildContext

namespace YY.Build.Graph.Nodes
{
    public class BuildBundleNode : BaseBuildNode
    {
        // 配置参数
        public string OutputPath = "StreamingRes/Bundles";
        public BuildAssetBundleOptions Compression = BuildAssetBundleOptions.ChunkBasedCompression;
        public BuildTarget TargetPlatform = BuildTarget.StandaloneWindows64;

        // UI 引用
        private TextField _pathField;
        private EnumField _compressionField;
        private EnumField _targetField;

        public override void Initialize()
        {
            title = "Export: Build AssetBundles";

            // 它是终点，只有输入
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            // --- 绘制配置 UI ---
            var container = new VisualElement();
            container.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            container.style.paddingTop = 5;
            container.style.paddingBottom = 5;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;

            // 1. 输出路径
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _pathField = new TextField("Output:") { value = OutputPath, style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(e => { OutputPath = e.newValue; NotifyChange(); });

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Directory", OutputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    // 尝试转为相对路径，方便团队协作
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    else if (path.Contains(Application.dataPath + "/../")) // 也就是项目根目录
                        path = path.Replace(Application.dataPath + "/../", "");

                    OutputPath = path;
                    _pathField.value = path;
                    NotifyChange();
                }
            })
            { text = "..." };

            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);

            // 2. 压缩格式
            _compressionField = new EnumField("Compression:", Compression);
            _compressionField.RegisterValueChangedCallback(e => { Compression = (BuildAssetBundleOptions)e.newValue; NotifyChange(); });

            // 3. 目标平台 (默认为当前平台)
            if (TargetPlatform == 0) TargetPlatform = EditorUserBuildSettings.activeBuildTarget;
            _targetField = new EnumField("Target:", TargetPlatform);
            _targetField.RegisterValueChangedCallback(e => { TargetPlatform = (BuildTarget)e.newValue; NotifyChange(); });

            // 4. 打包按钮 (大号)
            var buildBtn = new Button(OnBuildClick)
            {
                text = "BUILD",
                style = {
                    height = 30,
                    marginTop = 10,
                    backgroundColor = new Color(0.2f, 0.6f, 0.2f),
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };

            container.Add(pathRow);
            container.Add(_compressionField);
            container.Add(_targetField);
            container.Add(buildBtn);

            mainContainer.Add(container);
        }

        private void OnBuildClick()
        {
            // 1. 运行图逻辑，收集数据
            Debug.Log($"[BuildBundleNode] Collecting Assets...");
            var context = GraphRunner.Run(this);

            Debug.Log($"[BuildBundleNode] Assets collected: {context.Assets.Count}");

            // 2. 触发 SBP
            bool success = PipelineLauncher.Build(OutputPath, TargetPlatform, Compression, context.Assets);

            if (success)
            {
                EditorUtility.DisplayDialog("Build Success", $"Successfully built bundles to:\n{OutputPath}", "OK");
                EditorUtility.RevealInFinder(OutputPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Build Failed", "Check Console for details.", "OK");
            }
        }

        // --- 执行逻辑 ---
        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 作为终点节点，它不产生向下游的数据，只记录日志
            context.Logs.AppendLine($"[BuildBundleNode] Ready to build {context.Assets.Count} assets to {OutputPath}");
            return base.Execute(context);
        }

        // --- 序列化 ---
        [System.Serializable]
        class NodeData { public string outPath; public BuildAssetBundleOptions compress; public BuildTarget target; }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData { outPath = OutputPath, compress = Compression, target = TargetPlatform });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                OutputPath = data.outPath;
                Compression = data.compress;
                TargetPlatform = data.target;

                if (_pathField != null) _pathField.value = OutputPath;
                if (_compressionField != null) _compressionField.value = Compression;
                if (_targetField != null) _targetField.value = TargetPlatform;
            }
        }
    }
}