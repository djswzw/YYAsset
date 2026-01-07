using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Core;

namespace YY.Build.Graph.Nodes
{
    public class BatchBuildNode : BaseBuildNode
    {
        public override void Initialize()
        {
            base.Initialize();
            title = "Final: Batch Build";

            // 允许多个终点连进来
            AddInputPort("Wait For", UnityEditor.Experimental.GraphView.Port.Capacity.Multi);

            titleContainer.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f); // 红色背景，表示总开关

            var btn = new Button(OnBatchRun)
            {
                text = "EXECUTE ALL",
                style = { height = 40, fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold }
            };
            mainContainer.Add(btn);
        }

        private void OnBatchRun()
        {
            var graphView = GetFirstAncestorOfType<UnityEditor.Experimental.GraphView.GraphView>();
            var allNodes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<BaseBuildNode>(graphView.nodes.ToList()));

            Debug.Log(">>> Batch Build Started <<<");

            // 触发全图执行，IsBuildMode = true
            // GraphRunner 会先执行所有上游节点 (BuildBundle, BuildZip, BuildCopy) 的 Execute
            // 从而触发它们的打包逻辑
            GraphRunner.Run(this, allNodes, true);

            Debug.Log(">>> Batch Build Finished <<<");
        }

        public override System.Collections.Generic.Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine("[BatchBuildNode] All upstream tasks completed.");
            return base.Execute(context);
        }

        [System.Serializable] class NodeData { }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData());
        public override void LoadFromJSON(string json) { }
    }
}