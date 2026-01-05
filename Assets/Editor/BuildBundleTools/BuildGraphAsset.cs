using System;
using System.Collections.Generic;
using UnityEngine;

namespace YY.Build.Data
{
    /// <summary>
    /// 存储单个节点的数据
    /// </summary>
    [Serializable]
    public class BuildNodeData
    {
        public string NodeGUID;       // 唯一ID
        public string Title;          // 节点标题
        public Vector2 Position;      // 节点在画布上的位置
        public string NodeType;       // 具体的类名 (用于反射创建)
        public string JsonData;       // 节点内部数据的 JSON (如过滤规则、文件夹路径)
    }

    /// <summary>
    /// 存储连线的数据
    /// </summary>
    [Serializable]
    public class BuildEdgeData
    {
        public string BaseNodeGUID;   // 输出节点的 GUID
        public string BasePortName;   // 输出端口名
        public string TargetNodeGUID; // 输入节点的 GUID
        public string TargetPortName; // 输入端口名
    }

    /// <summary>
    /// 整个图的资产文件
    /// </summary>
    [CreateAssetMenu(fileName = "NewBuildGraph", menuName = "GameTools/Build Pipeline/Build Graph")]
    public class BuildGraphAsset : ScriptableObject
    {
        public List<BuildNodeData> Nodes = new List<BuildNodeData>();
        public List<BuildEdgeData> Edges = new List<BuildEdgeData>();
    }
}