using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace YY.Build.Graph.Nodes
{
    // --- 发送端 ---
    public class PortalSenderNode : BaseBuildNode
    {
        public string PortalID = "Global_Assets";
        private TextField _idField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Portal (Sender)";

            // 只有输入，没有输出（视觉上）
            AddInputPort("Input", Port.Capacity.Multi);

            _idField = new TextField("ID:");
            _idField.value = PortalID;
            _idField.RegisterValueChangedCallback(e => { PortalID = e.newValue; NotifyChange(); });

            // 样式：标红醒目
            titleContainer.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);

            mainContainer.Add(_idField);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            context.Logs.AppendLine($"[PortalSender] Broadcasting to ID: {PortalID}");
            // 数据通过 Output 端口传出去，虽然视觉上没有 Output 端口，
            // 但我们在 GraphRunner 里会建立逻辑连接到 Output
            return new Dictionary<string, BuildContext> { { "Output", context } };
        }

        [System.Serializable] class NodeData { public string id; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { id = PortalID });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { PortalID = data.id; if (_idField != null) _idField.value = PortalID; }
        }
    }

    // --- 接收端 ---
    public class PortalReceiverNode : BaseBuildNode
    {
        public string PortalID = "Global_Assets";
        private TextField _idField;

        public override void Initialize()
        {
            base.Initialize();
            title = "Portal (Receiver)";

            // 只有输出，没有输入（视觉上）
            AddOutputPort("Output", Port.Capacity.Multi);

            _idField = new TextField("ID:");
            _idField.value = PortalID;
            _idField.RegisterValueChangedCallback(e => { PortalID = e.newValue; NotifyChange(); });

            // 样式：标绿醒目
            titleContainer.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f);

            mainContainer.Add(_idField);
        }

        public override Dictionary<string, BuildContext> Execute(BuildContext context)
        {
            // 注意：GraphRunner 会通过 "VirtualInput" 这个逻辑端口把数据注进来
            // 我们需要主动去拉取这个虚拟端口的数据
            var inputData = GetInputContext("VirtualInput");

            inputData.Logs.AppendLine($"[PortalReceiver] Received from ID: {PortalID}");

            return new Dictionary<string, BuildContext> { { "Output", inputData } };
        }

        [System.Serializable] class NodeData { public string id; }
        public override string SaveToJSON() => JsonUtility.ToJson(new NodeData { id = PortalID });
        public override void LoadFromJSON(string json)
        {
            var data = JsonUtility.FromJson<NodeData>(json);
            if (data != null) { PortalID = data.id; if (_idField != null) _idField.value = PortalID; }
        }
    }
}