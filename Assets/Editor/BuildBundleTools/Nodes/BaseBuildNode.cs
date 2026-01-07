using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace YY.Build.Graph
{
    /// <summary>
    /// 所有打包节点的基类
    /// </summary>
    public class BaseBuildNode : Node
    {
        public string GUID;
        public Action OnDataChanged;
        public BaseBuildNode()
        {
            GUID = System.Guid.NewGuid().ToString();
            // 加载样式表 (确保该文件在 Resources 目录下)
            styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("BuildGraphStyles"));
        }

        public virtual void Initialize() { }

        // --- 核心执行逻辑 ---
        public virtual Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            return new Dictionary<string, BuildContext> { { "Output", context } };
        }
        protected void NotifyChange()
        {
            OnDataChanged?.Invoke();
        }
        // --- 辅助方法 ---

        public List<BaseBuildNode> GetInputNodes()
        {
            var nodes = new List<BaseBuildNode>();
            foreach (var element in inputContainer.Children())
            {
                if (element is Port inputPort && inputPort.connected)
                {
                    foreach (var edge in inputPort.connections)
                    {
                        if (edge.output.node is BaseBuildNode outputNode) nodes.Add(outputNode);
                    }
                }
            }
            return nodes;
        }

        protected Port AddInputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Input, capacity, typeof(bool));
            port.portName = name;
            port.name = name;
            inputContainer.Add(port);
            RefreshExpandedState();
            RefreshPorts();
            return port;
        }

        protected Port AddOutputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, typeof(bool));
            port.portName = name;
            port.name = name;
            outputContainer.Add(port);
            RefreshExpandedState();
            RefreshPorts();
            return port;
        }

        protected BuildContext GetInputContext(string portName)
        {
            var context = new BuildContext();
            var port = inputContainer.Q<Port>(portName);
            if (port == null || !port.connected) return context;

            foreach (var edge in port.connections)
            {
                var upstreamNode = edge.output.node as BaseBuildNode;
                var upstreamPort = edge.output.portName;

                if (Core.GraphRunner.DataMap != null &&
                    Core.GraphRunner.DataMap.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                    nodeOutputs.TryGetValue(upstreamPort, out var data))
                {
                    context.Assets.AddRange(data.Assets);
                    if (data.Logs.Length > 0) context.Logs.AppendLine(data.Logs.ToString());
                }
            }
            return context;
        }

        protected void ClearOutputPorts()
        {
            // 遍历 Output 容器中的所有子元素
            foreach (var element in outputContainer.Children())
            {
                if (element is Port port && port.connected)
                {
                    // 获取连接在端口上的所有线
                    // 注意：必须 ToList()，因为 Disconnect 会修改集合，不能在遍历时修改
                    var edgesToDelete = port.connections.ToList();

                    foreach (var edge in edgesToDelete)
                    {
                        // 1. 逻辑断开：断开 Input 和 Output 的数据连接
                        if (edge.input != null) edge.input.Disconnect(edge);
                        if (edge.output != null) edge.output.Disconnect(edge);

                        // 2. 视觉删除：从 GraphView 的视觉树中移除这根线
                        edge.RemoveFromHierarchy();
                    }
                }
            }

            // 清空容器
            outputContainer.Clear();

            // 刷新节点外观
            RefreshPorts();
            RefreshExpandedState();
        }

        // --- 序列化接口 ---
        public virtual string SaveToJSON() => "{}";
        public virtual void LoadFromJSON(string json) { }
    }

    public class TestLogNode : BaseBuildNode
    {
        public override void Initialize()
        {
            title = "Log Node";
            AddInputPort("Input");
        }
    }
}