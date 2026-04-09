using System;
using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic
{
    /// <summary>
    /// 可序列化的逻辑图资产。存储节点和连接，可保存为 ScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "NewLogicGraph", menuName = "HMI/Logic Graph")]
    public class LogicGraph : ScriptableObject
    {
        [SerializeField] private List<LogicNodeData> _nodes = new();
        [SerializeField] private List<LogicEdgeData> _edges = new();

        public IReadOnlyList<LogicNodeData> Nodes => _nodes;
        public IReadOnlyList<LogicEdgeData> Edges => _edges;

        public void Clear()
        {
            _nodes.Clear();
            _edges.Clear();
        }

        public void AddNode(LogicNodeData node) => _nodes.Add(node);
        public void RemoveNode(string nodeId) => _nodes.RemoveAll(n => n.Id == nodeId);

        public void AddEdge(LogicEdgeData edge) => _edges.Add(edge);
        public void RemoveEdge(string edgeId) => _edges.RemoveAll(e => e.Id == edgeId);

        public LogicNodeData FindNode(string nodeId) => _nodes.Find(n => n.Id == nodeId);
    }

    /// <summary>
    /// 序列化的节点数据。保存节点类型、位置、参数。
    /// </summary>
    [Serializable]
    public class LogicNodeData
    {
        public string Id = Guid.NewGuid().ToString();
        public string TypeName;          // 节点类型全名（如 "OnClick", "SetActive"）
        public Vector2 Position;         // GraphView 中的位置
        public string JsonData = "{}";   // 节点自定义参数（JSON）

        // ── GameObject 引用 ──
        public string TargetObjectPath;  // 场景路径（如 "Vehicle/Body/Door_FL"）
    }

    /// <summary>
    /// 序列化的连接数据。
    /// </summary>
    [Serializable]
    public class LogicEdgeData
    {
        public string Id = Guid.NewGuid().ToString();
        public string OutputNodeId;
        public string OutputPortName;
        public string InputNodeId;
        public string InputPortName;
    }
}
