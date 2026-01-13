using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Data;

namespace YY.Build.Graph.Nodes
{
    public class ReportNode : BaseBuildNode
    {
        public string ReportPath = "BuildReports";
        public bool OpenAfterBuild = true;

        private TextField _pathField;
        private Toggle _openToggle;

        public override void Initialize()
        {
            base.Initialize();
            title = "Final: Report & Analyze";

            // 接收来自 BuildBundle/Zip/Copy 的 Pass 端口
            AddInputPort("Input", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new Color(0.2f, 0.6f, 0.8f); // 蓝色背景

            var container = new VisualElement ();

            _pathField = new TextField("Save To:") { value = ReportPath };
            _pathField.RegisterValueChangedCallback(e => { ReportPath = e.newValue; NotifyChange(); });

            _openToggle = new Toggle("Open File After Build") { value = OpenAfterBuild };
            _openToggle.RegisterValueChangedCallback(e => { OpenAfterBuild = e.newValue; NotifyChange(); });

            // 手动生成按钮 (用于测试)
            var btn = new Button(OnGenerateClick) { text = "Generate Report Only", style = { marginTop = 5 } };
            var runBtn = new Button(() =>
            {
                var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
                var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));
                Core.GraphRunner.Run(this, allNodes, true);
            })
            {
                text = "RUN PIPELINE & REPORT",
                style = { height = 40, marginTop = 10, backgroundColor = new Color(0.2f, 0.6f, 0.2f), unityFontStyleAndWeight = FontStyle.Bold }
            };
            container.Add(runBtn);
            container.Add(_pathField);
            container.Add(_openToggle);
            container.Add(btn);
            mainContainer.Add(container);
        }

        private void OnGenerateClick()
        {
            // 手动触发时，可能没有 Reports 数据，只能测试 Preview
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));

            // 注意：如果想测试完整报告，应该跑 BatchBuildNode
            YY.Build.Core.GraphRunner.Run(this, allNodes, false);
        }

        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 只有在 Build 模式下，或者 Reports 有数据时才生成
            if (context.Reports.Count > 0 || context.IsBuildMode)
            {
                string filePath = GenerateMarkdownReport(context);
                context.Logs.AppendLine($"[ReportNode] Report saved to: {filePath}");

                if (OpenAfterBuild && File.Exists(filePath))
                {
                    EditorUtility.OpenWithDefaultApp(filePath);
                }
            }
            else
            {
                context.Logs.AppendLine("[ReportNode] No reports collected (Preview Mode or Empty).");
            }

            return base.Execute(context);
        }

        private string GenerateMarkdownReport(BuildContext context)
        {
            if (!Directory.Exists(ReportPath)) Directory.CreateDirectory(ReportPath);

            string fileName = $"BuildReport_{System.DateTime.Now:yyyyMMdd_HHmmss}.md";
            string fullPath = Path.Combine(ReportPath, fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Build Report");
            sb.AppendLine($"**Time:** {System.DateTime.Now}");
            sb.AppendLine($"**Total Assets Processed:** {context.Assets.Count}");
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine("| Task | Type | Assets | Size | Time (s) | Status |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            long totalSizeBytes = 0;
            double totalTime = 0;

            foreach (var r in context.Reports)
            {
                string statusIcon = r.IsSuccess ? "✅" : "❌";
                string sizeStr = EditorUtility.FormatBytes(r.OutputSizeBytes);

                sb.AppendLine($"| {r.NodeTitle} | {r.Category} | {r.AssetCount} | {sizeStr} | {r.DurationSeconds:F2}s | {statusIcon} |");

                totalSizeBytes += r.OutputSizeBytes;
                totalTime += r.DurationSeconds;
            }

            sb.AppendLine($"| **TOTAL** | | | **{EditorUtility.FormatBytes(totalSizeBytes)}** | **{totalTime:F2}s** | |");
            sb.AppendLine();

            sb.AppendLine("## Details");
            foreach (var r in context.Reports)
            {
                sb.AppendLine($"### {r.NodeTitle}");
                sb.AppendLine($"- **Output:** `{r.OutputPath}`");
                sb.AppendLine($"- **Message:** {r.Message}");
                sb.AppendLine();
            }

            File.WriteAllText(fullPath, sb.ToString());
            return fullPath;
        }

        [System.Serializable] class NodeData { public string path; public bool open; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { path = ReportPath, open = OpenAfterBuild });
        public override void LoadFromJSON(string json)
        {
            var d = JsonUtility.FromJson<NodeData>(json);
            if (d != null)
            {
                ReportPath = d.path; OpenAfterBuild = d.open;
                if (_pathField != null) _pathField.value = ReportPath;
                if (_openToggle != null) _openToggle.value = OpenAfterBuild;
            }
        }
    }
}