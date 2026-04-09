using System;
using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic
{
    /// <summary>
    /// 逻辑节点的运行时端口定义。
    /// </summary>
    public enum PortDirection { Input, Output }
    public enum PortCapacity { Single, Multi }

    /// <summary>
    /// 端口类型：Flow（执行流）或 Data（数据传递）。
    /// </summary>
    public enum PortKind { Flow, Data }

    [Serializable]
    public class PortDefinition
    {
        public string Name;
        public PortDirection Direction;
        public PortCapacity Capacity;
        public PortKind Kind;
        public Type DataType; // 仅 Data 类型端口使用

        public PortDefinition(string name, PortDirection dir, PortKind kind,
            PortCapacity capacity = PortCapacity.Single, Type dataType = null)
        {
            Name = name;
            Direction = dir;
            Kind = kind;
            Capacity = capacity;
            DataType = dataType;
        }
    }

    /// <summary>
    /// 节点分类（决定 GraphView 中的颜色和分组）。
    /// </summary>
    public enum NodeCategory
    {
        Trigger,    // 事件触发节点（绿色）
        Action,     // 动作执行节点（蓝色）
        Flow,       // 流程控制节点（橙色）
        Data        // 数据/条件节点（紫色）
    }

    /// <summary>
    /// 所有逻辑节点的运行时基类。
    /// 继承此类定义新节点，override Execute 实现逻辑。
    /// </summary>
    public abstract class LogicNodeBase
    {
        /// <summary>节点唯一 ID（与序列化数据对应）</summary>
        public string Id { get; set; }

        /// <summary>节点在场景中的目标对象</summary>
        public GameObject TargetObject { get; set; }

        /// <summary>节点分类</summary>
        public abstract NodeCategory Category { get; }

        /// <summary>节点显示名称</summary>
        public abstract string DisplayName { get; }

        /// <summary>节点描述（Tooltip 使用）</summary>
        public virtual string Description => "";

        /// <summary>定义此节点的所有端口</summary>
        public abstract List<PortDefinition> GetPorts();

        /// <summary>
        /// 执行节点逻辑。返回下一个要执行的 Flow 输出端口名称（null 表示结束）。
        /// </summary>
        public abstract string Execute(LogicExecutionContext context);

        /// <summary>
        /// 从 JSON 恢复节点参数。
        /// </summary>
        public virtual void Deserialize(string json) { }

        /// <summary>
        /// 将节点参数序列化为 JSON。
        /// </summary>
        public virtual string Serialize() => "{}";
    }

    /// <summary>
    /// 执行上下文，在整个图执行期间传递。
    /// </summary>
    public class LogicExecutionContext
    {
        /// <summary>当前逻辑图中的所有运行时节点实例</summary>
        public Dictionary<string, LogicNodeBase> NodeInstances { get; } = new();

        /// <summary>边连接查找表：(outputNodeId, outputPortName) → List of (inputNodeId, inputPortName)</summary>
        public Dictionary<(string, string), List<(string, string)>> Connections { get; } = new();

        /// <summary>数据端口的值传递：(nodeId, portName) → value</summary>
        public Dictionary<(string, string), object> PortValues { get; } = new();

        /// <summary>向数据端口写入值</summary>
        public void SetPortValue(string nodeId, string portName, object value)
        {
            PortValues[(nodeId, portName)] = value;
        }

        /// <summary>从数据端口读取值（沿连接回溯到上游输出端口）</summary>
        public T GetInputValue<T>(string nodeId, string inputPortName, T defaultValue = default)
        {
            // 先检查是否有直接设定的值
            if (PortValues.TryGetValue((nodeId, inputPortName), out var val) && val is T typed)
                return typed;

            // 回溯连接：找到谁连到了这个输入端口
            foreach (var kvp in Connections)
            {
                foreach (var (inNode, inPort) in kvp.Value)
                {
                    if (inNode == nodeId && inPort == inputPortName)
                    {
                        var (outNode, outPort) = kvp.Key;
                        if (PortValues.TryGetValue((outNode, outPort), out var upstreamVal) && upstreamVal is T upTyped)
                            return upTyped;
                    }
                }
            }

            return defaultValue;
        }
    }
}
