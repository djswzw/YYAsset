using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;

namespace YY.Build.Graph.Nodes
{
    public class BuildBundleNode : BaseBuildNode
    {
        // --- 数据 ---
        public string OutputPath = "StreamingRes/Bundles";
        public BuildTarget TargetPlatform = BuildTarget.StandaloneWindows64;
        public BuildAssetBundleOptions BuildOptions = BuildAssetBundleOptions.None;
        public string ManifestName = "sys_manifest";

        private TextField _pathField;
        private TextField _manifestField;
        private EnumField _targetField;
        private EnumField _compField;
        private Dictionary<BuildAssetBundleOptions, Toggle> _optionToggles = new Dictionary<BuildAssetBundleOptions, Toggle>();

        private enum CompressionType { LZMA_Default, LZ4_ChunkBased, Uncompressed }
        private CompressionType _compressionType = CompressionType.LZ4_ChunkBased;

        public override void Initialize()
        {
            title = "Export: Build AssetBundles";

            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            //初始化数据
            ParseOptionsToUI();

            //构建 UI
            DrawUI();
        }

        private void DrawUI()
        {
            var root = new VisualElement();
            root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            root.style.paddingTop = 5; root.style.paddingBottom = 5;
            root.style.paddingLeft = 5; root.style.paddingRight = 5;
            root.style.width = 260;

            // --- A. 输出路径 ---
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _pathField = new TextField("Output:") { value = OutputPath, style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(e => { OutputPath = e.newValue; NotifyChange(); });

            var browseBtn = new Button(() =>
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Directory", OutputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath)) path = "Assets" + path.Substring(Application.dataPath.Length);
                    else if (path.Contains(Application.dataPath + "/../")) path = path.Replace(Application.dataPath + "/../", "");
                    OutputPath = path;
                    _pathField.value = path;
                    NotifyChange();
                }
            })
            { text = "..." };
            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);

            _manifestField = new TextField("Manifest Name:") { value = ManifestName };
            _manifestField.RegisterValueChangedCallback(e => { ManifestName = e.newValue; NotifyChange(); });
            root.Add(_manifestField);

            // --- B. 平台 ---
            if (TargetPlatform == 0) TargetPlatform = EditorUserBuildSettings.activeBuildTarget;
            _targetField = new EnumField("Target:", TargetPlatform);
            _targetField.RegisterValueChangedCallback(e => { TargetPlatform = (BuildTarget)e.newValue; NotifyChange(); });

            // --- C. 压缩 ---
            _compField = new EnumField("Compression:", _compressionType);
            _compField.RegisterValueChangedCallback(e =>
            {
                _compressionType = (CompressionType)e.newValue;
                UpdateOptionsFromUI();
                NotifyChange();
            });

            // --- D. 常用开关 ---
            var forceToggle = CreateFlagToggle("Force Rebuild", BuildAssetBundleOptions.ForceRebuildAssetBundle);
            var hashToggle = CreateFlagToggle("Append Hash", BuildAssetBundleOptions.AppendHashToAssetBundleName);

            // --- E. 高级选项折叠 ---
            var foldout = new Foldout { text = "Advanced Options", value = false };
            foldout.style.marginTop = 5;
            foldout.Add(CreateFlagToggle("Strict Mode", BuildAssetBundleOptions.StrictMode));
            foldout.Add(CreateFlagToggle("Dry Run Build", BuildAssetBundleOptions.DryRunBuild));
            foldout.Add(CreateFlagToggle("Disable Write TypeTree", BuildAssetBundleOptions.DisableWriteTypeTree));
            foldout.Add(CreateFlagToggle("Ignore TypeTree Changes", BuildAssetBundleOptions.IgnoreTypeTreeChanges));

            // --- F. Build 按钮 ---
            var buildBtn = new Button(OnBuildClick)
            {
                text = "BUILD Bundles",
                style = { height = 30, marginTop = 10, backgroundColor = new Color(0.2f, 0.6f, 0.2f), unityFontStyleAndWeight = FontStyle.Bold }
            };

            root.Add(pathRow);
            root.Add(_targetField);
            root.Add(_compField);
            root.Add(forceToggle);
            root.Add(hashToggle);
            root.Add(foldout);
            root.Add(buildBtn);

            mainContainer.Add(root);
        }

        private Toggle CreateFlagToggle(string label, BuildAssetBundleOptions flag)
        {
            bool hasFlag = (BuildOptions & flag) != 0;
            var toggle = new Toggle(label) { value = hasFlag };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) BuildOptions |= flag;
                else BuildOptions &= ~flag;
                NotifyChange();
            });

            _optionToggles[flag] = toggle;

            return toggle;
        }

        private void OnBuildClick()
        {
            UpdateOptionsFromUI();
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = graphView.nodes.ToList().Cast<BaseBuildNode>().ToList();

            var context = GraphRunner.Run(this, allNodes);

            // 透传参数给 PipelineLauncher
            bool success = PipelineLauncher.Build(OutputPath, TargetPlatform, BuildOptions, context.Assets, ManifestName);

            //if (success) EditorUtility.RevealInFinder(OutputPath);
        }

        // --- 逻辑辅助 ---
        private void ParseOptionsToUI()
        {
            if ((BuildOptions & BuildAssetBundleOptions.ChunkBasedCompression) != 0) _compressionType = CompressionType.LZ4_ChunkBased;
            else if ((BuildOptions & BuildAssetBundleOptions.UncompressedAssetBundle) != 0) _compressionType = CompressionType.Uncompressed;
            else _compressionType = CompressionType.LZMA_Default;
        }

        private void UpdateOptionsFromUI()
        {
            BuildOptions &= ~BuildAssetBundleOptions.ChunkBasedCompression;
            BuildOptions &= ~BuildAssetBundleOptions.UncompressedAssetBundle;
            switch (_compressionType)
            {
                case CompressionType.LZ4_ChunkBased: BuildOptions |= BuildAssetBundleOptions.ChunkBasedCompression; break;
                case CompressionType.Uncompressed: BuildOptions |= BuildAssetBundleOptions.UncompressedAssetBundle; break;
            }
        }
        [Serializable] class NodeData { public string outPath; public BuildTarget target; public BuildAssetBundleOptions options; public string manifestName; }

        public override string SaveToJSON()
        {
            UpdateOptionsFromUI();
            return JsonUtility.ToJson(new NodeData { outPath = OutputPath, target = TargetPlatform, options = BuildOptions, manifestName = ManifestName });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null)
            {
                OutputPath = data.outPath;
                TargetPlatform = data.target;
                BuildOptions = data.options;
                ManifestName = string.IsNullOrEmpty(data.manifestName) ? "sys_manifest" : data.manifestName;
                ParseOptionsToUI();

                if (_pathField != null) _pathField.value = OutputPath;
                if (_manifestField != null) _manifestField.value = ManifestName;
                if (_targetField != null) _targetField.value = TargetPlatform;
                if (_compField != null) _compField.value = _compressionType;

                foreach (var kvp in _optionToggles)
                {
                    kvp.Value.value = (BuildOptions & kvp.Key) != 0;
                }
            }
        }

        // 终点节点不需要传递数据，只记录日志
        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[BuildBundleNode] Ready to build. Opts: {BuildOptions}");
            return base.Execute(context);
        }
    }
}