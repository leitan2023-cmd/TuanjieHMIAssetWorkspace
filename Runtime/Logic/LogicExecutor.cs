using System;
using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic
{
    /// <summary>
    /// 逻辑图运行时执行器。
    /// 挂载到场景 GameObject 上，加载 LogicGraph 并解释执行。
    /// </summary>
    public class LogicExecutor : MonoBehaviour
    {
        [Tooltip("要执行的逻辑图资产")]
        [SerializeField] private LogicGraph _graph;

        [Tooltip("启动时自动执行 OnLoad 触发器")]
        [SerializeField] private bool _autoExecuteOnLoad = true;

        private LogicExecutionContext _context;
        private readonly Dictionary<string, LogicNodeBase> _nodeInstances = new();
        private bool _initialized;

        public LogicGraph Graph
        {
            get => _graph;
            set
            {
                _graph = value;
                _initialized = false;
            }
        }

        private void Start()
        {
            if (_graph != null)
            {
                Initialize();
                if (_autoExecuteOnLoad)
                    ExecuteTrigger("OnLoad");
            }
        }

        /// <summary>
        /// 初始化执行器：实例化所有节点，建立连接查找表。
        /// </summary>
        public void Initialize()
        {
            if (_graph == null) return;

            _context = new LogicExecutionContext();
            _nodeInstances.Clear();

            // 实例化节点
            foreach (var nodeData in _graph.Nodes)
            {
                var node = LogicNodeRegistry.CreateNode(nodeData.TypeName);
                if (node == null)
                {
                    Debug.LogWarning($"[LogicExecutor] 未知节点类型: {nodeData.TypeName}");
                    continue;
                }

                node.Id = nodeData.Id;
                node.Deserialize(nodeData.JsonData);

                // 解析目标对象
                if (!string.IsNullOrEmpty(nodeData.TargetObjectPath))
                {
                    var target = GameObject.Find(nodeData.TargetObjectPath);
                    if (target == null)
                    {
                        // 尝试在当前 GameObject 的子层级中查找
                        var t = transform.Find(nodeData.TargetObjectPath);
                        target = t != null ? t.gameObject : null;
                    }
                    node.TargetObject = target;
                }

                _nodeInstances[nodeData.Id] = node;
                _context.NodeInstances[nodeData.Id] = node;
            }

            // 建立连接查找表
            foreach (var edge in _graph.Edges)
            {
                var key = (edge.OutputNodeId, edge.OutputPortName);
                if (!_context.Connections.ContainsKey(key))
                    _context.Connections[key] = new List<(string, string)>();
                _context.Connections[key].Add((edge.InputNodeId, edge.InputPortName));
            }

            _initialized = true;
        }

        /// <summary>
        /// 按名称执行指定类型的触发器节点。
        /// </summary>
        public void ExecuteTrigger(string triggerTypeName)
        {
            if (!_initialized) Initialize();
            if (_context == null) return;

            foreach (var kvp in _nodeInstances)
            {
                var node = kvp.Value;
                if (node.Category == NodeCategory.Trigger && node.GetType().Name == triggerTypeName)
                {
                    ExecuteFlow(node);
                }
            }
        }

        /// <summary>
        /// 从指定节点开始，沿 Flow 连接顺序执行。
        /// </summary>
        private void ExecuteFlow(LogicNodeBase startNode)
        {
            var current = startNode;
            int safetyCounter = 100; // 防止无限循环

            while (current != null && safetyCounter-- > 0)
            {
                try
                {
                    string nextPort = current.Execute(_context);

                    if (string.IsNullOrEmpty(nextPort))
                        break;

                    // 查找下一个节点
                    var key = (current.Id, nextPort);
                    if (_context.Connections.TryGetValue(key, out var targets) && targets.Count > 0)
                    {
                        // 执行第一个连接的节点（Flow 端口通常是单连接）
                        var (nextNodeId, _) = targets[0];
                        _context.NodeInstances.TryGetValue(nextNodeId, out current);
                    }
                    else
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LogicExecutor] 节点 {current.DisplayName}({current.Id}) 执行出错: {ex.Message}");
                    break;
                }
            }

            if (safetyCounter <= 0)
                Debug.LogWarning("[LogicExecutor] 执行链超过 100 步，已强制中断（可能存在循环）");
        }

        /// <summary>
        /// 外部调用：手动触发指定 ID 的节点执行链。
        /// 可用于 UI 按钮 onClick 等场景。
        /// </summary>
        public void TriggerNode(string nodeId)
        {
            if (!_initialized) Initialize();
            if (_nodeInstances.TryGetValue(nodeId, out var node))
                ExecuteFlow(node);
        }
    }
}
