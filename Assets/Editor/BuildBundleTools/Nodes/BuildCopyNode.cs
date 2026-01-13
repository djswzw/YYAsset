using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class BuildCopyNode : BaseBuildNode
    {
        public string OutputPath = "StreamingRes/Videos";
        private TextField _pathField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Export: Copy Files";
            // 关键：支持多入多出，实现串联
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);
            AddOutputPort("Pass", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new Color(0.6f, 0.4f, 0.2f); // 橙色背景

            var container = new VisualElement ();

            // 路径选择 UI
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _pathField = new TextField { value = OutputPath, style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(e => { OutputPath = e.newValue; NotifyChange(); });

            var btn = new Button(() =>
            {
                string p = EditorUtility.OpenFolderPanel("Select Output", OutputPath, "");
                if (!string.IsNullOrEmpty(p))
                {
                    OutputPath = MakeRelative(p);
                    _pathField.value = OutputPath;
                    NotifyChange();
                }
            })
            { text = "..." };

            row.Add(_pathField);
            row.Add(btn);

            // 手动触发按钮
            var buildBtn = new Button(OnBuildClick) { text = "COPY NOW", style = { marginTop = 5, backgroundColor = new Color(0.3f, 0.3f, 0.3f) } };

            container.Add(new Label("Target Directory:"));
            container.Add(row);
            container.Add(buildBtn);
            mainContainer.Add(container);
        }

        private string MakeRelative(string path)
        {
            if (path.StartsWith(Application.dataPath)) return "Assets" + path.Substring(Application.dataPath.Length);
            return path;
        }

        private void OnBuildClick()
        {
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));
            Core.GraphRunner.Run(this, allNodes, true);
        }

        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[BuildCopyNode] Received {context.Assets.Count} files.");

            if (context.IsBuildMode)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (!Directory.Exists(OutputPath)) Directory.CreateDirectory(OutputPath);

                long totalSize = 0;
                foreach (var asset in context.Assets)
                {
                    string src = Path.GetFullPath(asset.AssetPath);
                    string dest = Path.Combine(OutputPath, Path.GetFileName(asset.AssetPath));

                    if (File.Exists(src))
                    {
                        File.Copy(src, dest, true);
                        totalSize += new FileInfo(dest).Length;
                    }
                }
                watch.Stop();

                context.Reports.Add(new BuildReportItem
                {
                    NodeTitle = title,
                    Category = "Raw Copy",
                    OutputPath = OutputPath,
                    AssetCount = context.Assets.Count,
                    OutputSizeBytes = totalSize,
                    DurationSeconds = watch.Elapsed.TotalSeconds,
                    IsSuccess = true,
                    Message = "OK"
                });

                context.Logs.AppendLine($"[BuildCopyNode] Copied. Size: {EditorUtility.FormatBytes(totalSize)}");
            }
            else
            {
                context.Logs.AppendLine("[BuildCopyNode] Preview Mode (No files copied).");
            }

            // 透传数据给下游
            return new System.Collections.Generic.Dictionary<string, BuildContext> { { "Pass", context } };
        }

        [System.Serializable] class NodeData { public string path; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { path = OutputPath });
        public override void LoadFromJSON(string json)
        {
            var d = JsonUtility.FromJson<NodeData>(json);
            if (d != null) { OutputPath = d.path; if (_pathField != null) _pathField.value = OutputPath; }
        }
    }
}