using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph
{
    public class BuildGraphView : GraphView
    {
        public System.Func<GraphViewChange, GraphViewChange> OnGraphViewChanged;

        public BuildGraphView()
        {
            // 1. 基础交互配置
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // 2. 添加网格背景
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // 3. 右键菜单：创建节点
            // 这里暂时硬编码添加两个测试节点，后续阶段会做成 SearchWindow
            var menu = new ContextualMenuManipulator(evt =>
            {
                //evt.menu.AppendAction("Add Source Node", action => CreateNode<TestSourceNode>(action.eventInfo.localMousePosition));
                evt.menu.AppendAction("Add Log Node", action => CreateNode<TestLogNode>(action.eventInfo.localMousePosition));
            });
            this.AddManipulator(menu);
            this.graphViewChanged = OnChange;
        }

        private GraphViewChange OnChange(GraphViewChange change)
        {
            if (OnGraphViewChanged != null) return OnGraphViewChanged(change);
            return change;
        }

        // 创建节点的辅助方法
        public void CreateNode<T>(Vector2 position) where T : BaseBuildNode, new()
        {
            var node = new T();
            node.Initialize();
            node.SetPosition(new Rect(position, Vector2.zero));
            AddElement(node);
        }

        // 【关键】定义端口兼容规则：所有端口都能连 (暂时)
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            ports.ForEach(port =>
            {
                // 不能连自己，输入连输出，输出连输入
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    compatiblePorts.Add(port);
                }
            });
            return compatiblePorts;
        }
    }
}