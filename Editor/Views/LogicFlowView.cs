using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.LogicFlowEditor;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// Logic Flow 视图 — 嵌入 HMI Workspace 主窗口的逻辑编辑面板。
    /// 在 ViewMode.LogicFlow 时显示。
    /// </summary>
    public class LogicFlowView
    {
        private VisualElement _root;
        private LogicGraphView _graphView;
        private LogicController _controller;
        private WorkspaceState _state;
        private Label _statusLabel;

        public void Bind(VisualElement container, WorkspaceState state, LogicController controller)
        {
            _root = container;
            _state = state;
            _controller = controller;

            BuildUI();
            SubscribeEvents();
        }

        private void BuildUI()
        {
            _root.Clear();

            // ── 工具栏 ──
            var toolbar = new VisualElement();
            toolbar.AddToClassList("logic-toolbar");
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.height = 32;
            toolbar.style.backgroundColor = new StyleColor(new Color(0.14f, 0.14f, 0.17f));
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new StyleColor(new Color(0.08f, 0.08f, 0.1f));

            var title = new Label("Logic Flow Editor");
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f));
            title.style.flexGrow = 1;
            toolbar.Add(title);

            var newBtn = CreateToolbarButton("新建", CreateNewGraph);
            toolbar.Add(newBtn);

            var loadBtn = CreateToolbarButton("加载", LoadGraph);
            toolbar.Add(loadBtn);

            var saveBtn = CreateToolbarButton("保存", SaveGraph);
            toolbar.Add(saveBtn);

            var openFullBtn = CreateToolbarButton("独立窗口", OpenFullEditor);
            toolbar.Add(openFullBtn);

            var execBtn = CreateToolbarButton("▶ 测试", ExecuteGraph);
            execBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.5f, 0.3f));
            execBtn.style.color = new StyleColor(Color.white);
            toolbar.Add(execBtn);

            _root.Add(toolbar);

            // ── 状态栏 ──
            _statusLabel = new Label("右键空白处可添加节点");
            _statusLabel.style.height = 20;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.paddingLeft = 8;
            _statusLabel.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.14f));
            _statusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            _root.Add(_statusLabel);

            // ── GraphView 区域 ──
            _graphView = new LogicGraphView();
            _graphView.style.flexGrow = 1;
            _graphView.OnGraphChanged = () =>
            {
                _statusLabel.text = "● 有未保存的更改";
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.8f, 0.3f));
            };
            _root.Add(_graphView);

            // 设置宿主窗口引用（确保 SearchWindow 等能正确弹出）
            var hostWindow = EditorWindow.focusedWindow;
            if (hostWindow != null)
                _graphView.SetHostWindow(hostWindow);

            // 加载当前控制器的图
            if (_controller.CurrentGraph != null)
                _graphView.LoadGraph(_controller.CurrentGraph);
        }

        private Button CreateToolbarButton(string text, System.Action action)
        {
            var btn = new Button(action) { text = text };
            btn.style.height = 22;
            btn.style.marginLeft = 2;
            btn.style.marginRight = 2;
            btn.style.fontSize = 11;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            return btn;
        }

        private void SubscribeEvents()
        {
            LogicEvents.GraphLoaded.Subscribe(OnGraphLoaded);
            LogicEvents.ExecutionCompleted.Subscribe(OnExecutionCompleted);
        }

        private void OnGraphLoaded(LogicGraphLoadedEvent evt)
        {
            _graphView?.LoadGraph(evt.Graph);
            _statusLabel.text = $"已加载: {evt.Graph.name} ({evt.Graph.Nodes.Count} 个节点)";
            _statusLabel.style.color = new StyleColor(new Color(0.4f, 0.8f, 0.5f));
        }

        private void OnExecutionCompleted(LogicExecutionCompletedEvent evt)
        {
            _statusLabel.text = evt.Success
                ? $"✓ 执行完成 ({evt.TriggerName})"
                : $"✗ 执行失败 ({evt.TriggerName})";
            _statusLabel.style.color = new StyleColor(
                evt.Success ? new Color(0.4f, 0.8f, 0.5f) : new Color(0.9f, 0.3f, 0.3f));
        }

        private void CreateNewGraph()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "创建 Logic Graph", "NewLogicGraph", "asset", "选择保存位置");
            if (!string.IsNullOrEmpty(path))
                _controller.CreateGraph(path);
        }

        private void LoadGraph()
        {
            var path = EditorUtility.OpenFilePanel("选择 Logic Graph", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);
            var graph = AssetDatabase.LoadAssetAtPath<Runtime.Logic.LogicGraph>(path);
            if (graph != null) _controller.LoadGraph(graph);
        }

        private void SaveGraph()
        {
            _graphView?.SaveGraph();
            AssetDatabase.SaveAssets();
            _statusLabel.text = "已保存";
            _statusLabel.style.color = new StyleColor(new Color(0.4f, 0.8f, 0.5f));
        }

        private void OpenFullEditor()
        {
            if (_controller.CurrentGraph != null)
                LogicGraphEditorWindow.OpenWithGraph(_controller.CurrentGraph);
            else
                LogicGraphEditorWindow.OpenWindow();
        }

        private void ExecuteGraph()
        {
            SaveGraph();
            _controller.ExecuteInEditor();
        }

        public void Dispose()
        {
            LogicEvents.GraphLoaded.Unsubscribe(OnGraphLoaded);
            LogicEvents.ExecutionCompleted.Unsubscribe(OnExecutionCompleted);
        }
    }
}
