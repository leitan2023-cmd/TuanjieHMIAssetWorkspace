using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.LogicFlowEditor
{
    /// <summary>
    /// Logic Flow Editor 的核心 GraphView 实现。
    /// 提供节点创建、连接、删除、复制等交互能力。
    /// </summary>
    public class LogicGraphView : GraphView
    {
        private LogicGraph _graphAsset;
        private readonly LogicNodeSearchWindow _searchWindow;

        public Action OnGraphChanged;

        private EditorWindow _hostWindow;
        private Vector2 _lastRightClickScreenPos;

        public LogicGraphView()
        {
            // ── 基础设置 ──
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // 背景网格
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // 搜索窗口
            _searchWindow = ScriptableObject.CreateInstance<LogicNodeSearchWindow>();
            _searchWindow.Initialize(this);

            // nodeCreationRequest（独立窗口模式下生效）
            nodeCreationRequest = ctx => OpenSearchWindow(ctx.screenMousePosition);

            // 右键菜单：通过 buildContextualMenu 确保在嵌入模式下也能添加节点
            this.RegisterCallback<ContextualMenuPopulateEvent>(OnBuildContextMenu);

            // 样式
            var style = Resources.Load<StyleSheet>("LogicGraphStyles");
            if (style != null) styleSheets.Add(style);
        }

        /// <summary>设置宿主窗口引用（嵌入模式下需要）</summary>
        public void SetHostWindow(EditorWindow window) => _hostWindow = window;

        private void OpenSearchWindow(Vector2 screenPos)
        {
            _lastRightClickScreenPos = screenPos;
            SearchWindow.Open(new SearchWindowContext(screenPos), _searchWindow);
        }

        private void OnBuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            // 获取已注册的节点，按分类添加到右键菜单
            var groups = LogicNodeRegistry.GetAllGrouped();
            var categoryNames = new Dictionary<NodeCategory, string>
            {
                { NodeCategory.Trigger, "触发器" },
                { NodeCategory.Action, "动作" },
                { NodeCategory.Flow, "流程" },
                { NodeCategory.Data, "数据" },
            };
            var order = new[] { NodeCategory.Trigger, NodeCategory.Action, NodeCategory.Flow, NodeCategory.Data };

            // 记录鼠标位置用于创建节点
            var mousePos = evt.mousePosition;

            foreach (var category in order)
            {
                if (!groups.TryGetValue(category, out var nodeList)) continue;
                var catName = categoryNames.ContainsKey(category) ? categoryNames[category] : category.ToString();

                foreach (var (typeName, displayName, _) in nodeList)
                {
                    evt.menu.AppendAction(
                        $"添加节点/{catName}/{displayName}",
                        action =>
                        {
                            // 将鼠标位置转换为 content 坐标
                            var contentOffset = contentViewContainer.transform.position;
                            var contentScale = contentViewContainer.transform.scale;
                            var localPos = new Vector2(
                                (mousePos.x - contentOffset.x) / contentScale.x,
                                (mousePos.y - contentOffset.y) / contentScale.y
                            );
                            CreateNode(typeName, localPos);
                        });
                }
            }
        }

        /// <summary>
        /// 加载 LogicGraph 资产并还原所有节点和连接。
        /// </summary>
        public void LoadGraph(LogicGraph graph)
        {
            _graphAsset = graph;
            ClearGraph();

            if (graph == null) return;

            // 还原节点
            foreach (var nodeData in graph.Nodes)
            {
                var nodeView = CreateNodeView(nodeData);
                if (nodeView != null)
                    AddElement(nodeView);
            }

            // 还原连接
            foreach (var edgeData in graph.Edges)
            {
                var outputNode = GetNodeById(edgeData.OutputNodeId);
                var inputNode = GetNodeById(edgeData.InputNodeId);

                if (outputNode == null || inputNode == null) continue;

                var outputPort = outputNode.GetOutputPort(edgeData.OutputPortName);
                var inputPort = inputNode.GetInputPort(edgeData.InputPortName);

                if (outputPort == null || inputPort == null) continue;

                var edge = outputPort.ConnectTo(inputPort);
                AddElement(edge);
            }
        }

        /// <summary>
        /// 将当前 GraphView 状态保存回 LogicGraph 资产。
        /// </summary>
        public void SaveGraph()
        {
            if (_graphAsset == null) return;

            Undo.RecordObject(_graphAsset, "Save Logic Graph");
            _graphAsset.Clear();

            // 保存节点
            foreach (var node in nodes.ToList())
            {
                if (node is LogicNodeView lnv)
                {
                    _graphAsset.AddNode(lnv.ToData());
                }
            }

            // 保存连接
            foreach (var edge in edges.ToList())
            {
                if (edge.output?.node is LogicNodeView outputNode && edge.input?.node is LogicNodeView inputNode)
                {
                    _graphAsset.AddEdge(new LogicEdgeData
                    {
                        OutputNodeId = outputNode.NodeId,
                        OutputPortName = edge.output.portName,
                        InputNodeId = inputNode.NodeId,
                        InputPortName = edge.input.portName
                    });
                }
            }

            EditorUtility.SetDirty(_graphAsset);
        }

        /// <summary>
        /// 在指定位置创建新节点。
        /// </summary>
        public LogicNodeView CreateNode(string typeName, Vector2 position)
        {
            var nodeInstance = LogicNodeRegistry.CreateNode(typeName);
            if (nodeInstance == null) return null;

            var data = new LogicNodeData
            {
                TypeName = typeName,
                Position = position,
            };

            var nodeView = CreateNodeView(data);
            if (nodeView != null)
            {
                AddElement(nodeView);
                OnGraphChanged?.Invoke();
            }

            return nodeView;
        }

        private LogicNodeView CreateNodeView(LogicNodeData data)
        {
            var nodeInstance = LogicNodeRegistry.CreateNode(data.TypeName);
            if (nodeInstance == null) return null;

            nodeInstance.Id = data.Id;
            nodeInstance.Deserialize(data.JsonData);

            var view = new LogicNodeView(data, nodeInstance);
            view.SetPosition(new Rect(data.Position, Vector2.zero));
            return view;
        }

        private LogicNodeView GetNodeById(string id)
        {
            return nodes.ToList()
                .OfType<LogicNodeView>()
                .FirstOrDefault(n => n.NodeId == id);
        }

        private void ClearGraph()
        {
            foreach (var edge in edges.ToList()) RemoveElement(edge);
            foreach (var node in nodes.ToList()) RemoveElement(node);
        }

        /// <summary>
        /// 端口兼容性检查：只允许 Flow→Flow 或同类型 Data→Data 连接。
        /// </summary>
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(endPort =>
                endPort.direction != startPort.direction &&
                endPort.node != startPort.node &&
                endPort.portType == startPort.portType
            ).ToList();
        }

        /// <summary>
        /// 处理图变更（节点移动、连接变化等）。
        /// </summary>
        public override EventPropagation DeleteSelection()
        {
            var result = base.DeleteSelection();
            OnGraphChanged?.Invoke();
            return result;
        }
    }
}
