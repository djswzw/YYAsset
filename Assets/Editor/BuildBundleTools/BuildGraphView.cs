using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using YY.Build.Graph;

namespace YY.Build.Graph
{
    public class BuildGraphView : GraphView
    {
        public Func<GraphViewChange, GraphViewChange> OnGraphViewChanged;

        public BuildGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            this.graphViewChanged = OnChange;

            serializeGraphElements = SerializeGraphElementsImpl;
            unserializeAndPaste = UnserializeAndPasteImpl;
        }

        private GraphViewChange OnChange(GraphViewChange change)
        {
            if (OnGraphViewChanged != null) return OnGraphViewChanged(change);
            return change;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            ports.ForEach(port =>
            {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    compatiblePorts.Add(port);
                }
            });
            return compatiblePorts;
        }

        // --- 复制粘贴数据结构 ---
        [Serializable]
        private class CopyPasteContainer
        {
            public List<string> NodeTypes = new List<string>();
            public List<string> JsonDatas = new List<string>();
        }

        // --- 1. 复制逻辑 ---
        private string SerializeGraphElementsImpl(IEnumerable<GraphElement> elements)
        {
            var container = new CopyPasteContainer();

            foreach (var element in elements)
            {
                if (element is BaseBuildNode node)
                {
                    container.NodeTypes.Add(node.GetType().FullName);
                    container.JsonDatas.Add(node.SaveToJSON());
                }
                // 暂时只支持复制节点，不复制连线（因为连线需要两端都在），简化逻辑
            }

            return JsonUtility.ToJson(container);
        }

        // --- 2. 粘贴逻辑 ---
        private void UnserializeAndPasteImpl(string operationName, string data)
        {
            var container = JsonUtility.FromJson<CopyPasteContainer>(data);
            if (container == null || container.NodeTypes.Count == 0) return;

            ClearSelection();

            // 计算粘贴位置的偏移量 (鼠标位置或中心偏移)
            Vector2 center = contentViewContainer.WorldToLocal(layout.center);

            for (int i = 0; i < container.NodeTypes.Count; i++)
            {
                string typeName = container.NodeTypes[i];
                string json = container.JsonDatas[i];

                var nodeType = Type.GetType(typeName);
                if (nodeType == null) nodeType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == typeName);
                if (nodeType == null) continue;

                var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
                node.Initialize();

                // 【关键】生成新 GUID，防止 ID 冲突
                node.GUID = Guid.NewGuid().ToString();

                // 还原数据
                node.LoadFromJSON(json);

                // 设置位置 (稍微偏移一点，避免重叠)
                // 这里简单处理：粘贴到视图中心附近 + 随机偏移
                Rect rect = node.GetPosition();
                rect.position = center + new Vector2(i * 20, i * 20);
                node.SetPosition(rect);

                AddElement(node);
                AddToSelection(node);
            }
        }
    }
}