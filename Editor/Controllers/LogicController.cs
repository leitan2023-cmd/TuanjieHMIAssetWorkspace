using UnityEditor;
using UnityEngine;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// Logic Flow 控制器。
    /// 管理 LogicGraph 资产的创建、加载和执行，桥接 Editor UI 和 Runtime 执行器。
    /// </summary>
    public sealed class LogicController : IController
    {
        private readonly WorkspaceState _state;
        private LogicGraph _currentGraph;
        private LogicExecutor _executor;

        public LogicGraph CurrentGraph => _currentGraph;

        public LogicController(WorkspaceState state)
        {
            _state = state;
        }

        public void Initialize()
        {
            // 确保节点注册表已初始化
            LogicNodeRegistry.Refresh();
            Debug.Log($"[LogicController] 已注册 {LogicNodeRegistry.RegisteredTypes.Count} 种逻辑节点");
        }

        /// <summary>
        /// 创建新的 LogicGraph 资产。
        /// </summary>
        public LogicGraph CreateGraph(string path)
        {
            var graph = ScriptableObject.CreateInstance<LogicGraph>();
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            _currentGraph = graph;

            LogicEvents.GraphLoaded.Publish(new LogicGraphLoadedEvent(graph));
            _state.StatusMessage.Value = $"已创建逻辑图: {graph.name}";
            return graph;
        }

        /// <summary>
        /// 加载现有的 LogicGraph 资产。
        /// </summary>
        public void LoadGraph(LogicGraph graph)
        {
            _currentGraph = graph;
            LogicEvents.GraphLoaded.Publish(new LogicGraphLoadedEvent(graph));
            _state.StatusMessage.Value = $"已加载逻辑图: {graph.name}";
        }

        /// <summary>
        /// 在 Editor 中测试执行当前图。
        /// </summary>
        public void ExecuteInEditor(string triggerName = "OnLoadTrigger")
        {
            if (_currentGraph == null)
            {
                _state.StatusMessage.Value = "请先加载逻辑图";
                return;
            }

            // 在场景中查找或创建执行器
            _executor = Object.FindObjectOfType<LogicExecutor>();
            if (_executor == null)
            {
                var go = new GameObject("_LogicExecutor");
                _executor = go.AddComponent<LogicExecutor>();
                Undo.RegisterCreatedObjectUndo(go, "Create LogicExecutor");
            }

            _executor.Graph = _currentGraph;
            _executor.Initialize();
            _executor.ExecuteTrigger(triggerName);

            LogicEvents.ExecutionCompleted.Publish(new LogicExecutionCompletedEvent(triggerName, true));
            _state.StatusMessage.Value = $"逻辑图执行完成 ({triggerName})";
        }

        /// <summary>
        /// 为当前选中的 GameObject 绑定点击触发器（快捷操作）。
        /// </summary>
        public void BindClickTrigger(GameObject target)
        {
            if (_currentGraph == null || target == null) return;

            // 获取目标的场景路径
            var path = GetGameObjectPath(target);

            _currentGraph.AddNode(new LogicNodeData
            {
                TypeName = "OnClickTrigger",
                Position = new Vector2(100, 100),
                TargetObjectPath = path
            });

            EditorUtility.SetDirty(_currentGraph);
            LogicEvents.GraphChanged.Publish(new LogicGraphChangedEvent());
            _state.StatusMessage.Value = $"已为 {target.name} 添加点击触发器";
        }

        private string GetGameObjectPath(GameObject obj)
        {
            var path = obj.name;
            var parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        public void Dispose()
        {
            _currentGraph = null;
            _executor = null;
        }
    }
}
