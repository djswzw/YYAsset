using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph
{
    /// <summary>
    /// 所有打包节点的基类
    /// </summary>
    public class BaseBuildNode : Node
    {
        public string GUID;

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

        // 【关键修复】 清理端口时，必须同步清理连接在上面的线 (Edge)
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

    // --- 实现一个简单的测试节点，方便验证 ---
    public class TestSourceNode : BaseBuildNode
    {
        public string FolderPath = "Assets";

        public override void Initialize()
        {
            title = "Source Directory";

            // 输出端口
            AddOutputPort("Output");

            // --- UI 构建 ---
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row; // 水平布局
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            container.style.paddingTop = 5;
            container.style.paddingBottom = 5;

            // 1. 文本框 (占满剩余空间)
            var textField = new TextField();
            textField.value = FolderPath;
            textField.style.flexGrow = 1; // 自动拉伸
            textField.RegisterValueChangedCallback(evt => FolderPath = evt.newValue);

            // 2. 浏览按钮
            var browseBtn = new Button(() =>
            {
                // 打开文件夹选择面板
                string path = UnityEditor.EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // 将绝对路径转换为 Unity 相对路径 (Assets/...)
                    int index = path.IndexOf("Assets");
                    if (index >= 0)
                    {
                        path = path.Substring(index);
                    }

                    // 更新数据和 UI
                    FolderPath = path;
                    textField.value = path;
                }
            })
            { text = "..." }; // 按钮文字

            browseBtn.style.width = 30; // 固定宽度

            container.Add(textField);
            container.Add(browseBtn);

            mainContainer.Add(container);
        }

        [System.Serializable]
        class NodeData { public string path; }

        public override string SaveToJSON()
        {
            return JsonUtility.ToJson(new NodeData { path = FolderPath });
        }

        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) FolderPath = data.path;
        }
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