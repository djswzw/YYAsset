using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph
{
    public class BaseBuildNode : Node
    {
        public string GUID;
        public Action OnDataChanged;

        // Key: 本节点的 InputPortName, Value: List<(上游节点, 上游OutputPortName)>
        private Dictionary<string, List<(BaseBuildNode, string)>> _upstreamConnections = new Dictionary<string, List<(BaseBuildNode, string)>>();

        public BaseBuildNode()
        {
            GUID = System.Guid.NewGuid().ToString();
            styleSheets.Add(UnityEngine.Resources.Load<StyleSheet>("BuildGraphStyles"));
            capabilities |= Capabilities.Renamable;
        }

        public virtual void Initialize()
        {
            var titleLabel = titleContainer.Q<Label>("title-label");

            // 2. 注册鼠标点击事件到标题栏
            titleContainer.RegisterCallback<MouseDownEvent>(evt =>
            {
                // 检测双击 (左键 + clickCount=2)
                if (evt.button == 0 && evt.clickCount == 2)
                {
                    OpenRenameTextField();
                    evt.StopPropagation();
                    focusController.IgnoreEvent(evt);
                }
            });
        }
        private void OpenRenameTextField()
        {
            // 1. 创建临时输入框
            var textField = new TextField();
            textField.value = title;
            textField.style.position = Position.Absolute;
            textField.style.left = 0;
            textField.style.top = 0;
            textField.style.right = 0;
            textField.style.height = titleContainer.layout.height; // 盖住原标题
            textField.style.fontSize = 12;
            textField.style.marginLeft = 5;
            textField.style.marginRight = 5;

            // 样式调整：让它看起来像原生输入框
            textField.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);

            // 2. 添加到 titleContainer
            titleContainer.Add(textField);

            // 3. 聚焦并全选
            textField.Focus();
            // 注意：SelectAll 需要在下一帧执行，否则可能无效
            textField.schedule.Execute(() => textField.SelectAll());

            // 4. 定义完成逻辑
            void SaveAndClose()
            {
                if (!string.IsNullOrEmpty(textField.value) && textField.value != title)
                {
                    title = textField.value;
                    NotifyChange(); // 触发 Undo 保存
                }
                titleContainer.Remove(textField); // 销毁输入框
            }

            // 5. 注册提交和取消事件
            textField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SaveAndClose();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    titleContainer.Remove(textField); // 取消
                    evt.StopPropagation();
                }
            });

            // 失去焦点时自动保存
            textField.RegisterCallback<FocusOutEvent>(evt => SaveAndClose());
        }

        // --- 核心执行逻辑 ---
        public virtual Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            return new Dictionary<string, BuildContext> { { "Output", context } };
        }

        // 在运行前，由 Runner (Editor或Headless) 调用此方法建立逻辑连接
        public void ResetConnections()
        {
            _upstreamConnections.Clear();
        }

        public void AddUpstreamConnection(string inputPortName, BaseBuildNode sourceNode, string sourcePortName)
        {
            if (!_upstreamConnections.ContainsKey(inputPortName))
                _upstreamConnections[inputPortName] = new List<(BaseBuildNode, string)>();

            _upstreamConnections[inputPortName].Add((sourceNode, sourcePortName));
        }

        public Dictionary<string, List<(BaseBuildNode, string)>> GetUpstreamConnections()
        {
            return _upstreamConnections;
        }

        protected BuildContext GetInputContext(string portName)
        {
            var context = new BuildContext();

            if (_upstreamConnections.TryGetValue(portName, out var connections))
            {
                foreach (var (upstreamNode, upstreamPort) in connections)
                {
                    // 从 GraphRunner 的全局缓存中取数据
                    if (YY.Build.Core.GraphRunner.DataMap != null &&
                        YY.Build.Core.GraphRunner.DataMap.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                        nodeOutputs.TryGetValue(upstreamPort, out var data))
                    {
                        context.Assets.AddRange(data.Assets);
                        if (data.Logs.Length > 0) context.Logs.AppendLine(data.Logs.ToString());
                    }
                }
            }
            return context;
        }

        // --- 辅助方法 (获取上游节点对象，用于拓扑排序) ---
        public List<BaseBuildNode> GetInputNodes()
        {
            var nodes = new List<BaseBuildNode>();
            foreach (var list in _upstreamConnections.Values)
            {
                foreach (var (node, _) in list) nodes.Add(node);
            }
            return nodes;
        }

        // --- UI 端口辅助 (保持不变) ---
        protected Port AddInputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Input, capacity, typeof(bool));
            port.portName = name; port.name = name;
            inputContainer.Add(port);
            RefreshExpandedState(); RefreshPorts();
            return port;
        }

        protected Port AddOutputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, typeof(bool));
            port.portName = name; port.name = name;
            outputContainer.Add(port);
            RefreshExpandedState(); RefreshPorts();
            return port;
        }

        protected void ClearOutputPorts()
        {
            foreach (var element in outputContainer.Children())
            {
                if (element is Port port && port.connected)
                {
                    var edgesToDelete = port.connections.ToList();
                    foreach (var edge in edgesToDelete)
                    {
                        if (edge.input != null) edge.input.Disconnect(edge);
                        if (edge.output != null) edge.output.Disconnect(edge);
                        edge.RemoveFromHierarchy();
                    }
                }
            }
            outputContainer.Clear();
            RefreshPorts(); RefreshExpandedState();
        }

        protected void NotifyChange() => OnDataChanged?.Invoke();

        // 序列化
        public virtual string SaveToJSON() => "{}";
        public virtual void LoadFromJSON(string json) { }
    }
}