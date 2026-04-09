using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;
using HMI.Workspace.Editor.LogicFlowEditor;

namespace HMI.Workspace.Editor.Views
{
    public sealed class HMIWorkspaceWindow : EditorWindow
    {
        private WorkspaceState _state;
        private WorkspaceController _workspaceController;
        private ISelectionService _selectionService;
        private VehicleSetupView _vehicleSetupInstance;
        private VisualElement _homeView;
        private VisualElement _assetBrowserView;
        private VisualElement _scenePreviewView;
        private VisualElement _compareView;
        private VisualElement _vehicleSetupView;
        private VisualElement _batchReplaceView;
        private VisualElement _sceneBuilderView;
        private VisualElement _logicFlowView;
        private VisualElement _previewTabContent;
        private VisualElement _aiTabContent;
        private Button _previewTabButton;
        private Button _aiTabButton;

        // ── 懒初始化标记：非首屏 View 在首次切换时才 Bind ──
        private bool _compareBound;
        private bool _vehicleSetupBound;
        private bool _batchReplaceBound;
        private bool _sceneBuilderBound;
        private bool _logicFlowBound;
        private bool _scenePreviewBound;

        [MenuItem("Window/HMI Asset Workspace")]
        public static void Open()
        {
            var window = GetWindow<HMIWorkspaceWindow>();
            window.titleContent = new GUIContent("HMI Asset Studio");
            window.minSize = new UnityEngine.Vector2(1200, 700);
        }

        [MenuItem("Window/HMI Asset Workspace/Dump PerfTrace Report")]
        public static void DumpPerfReport() => Core.PerfTrace.DumpReport();

        [MenuItem("Window/HMI Asset Workspace/Reset PerfTrace")]
        public static void ResetPerfTrace() => Core.PerfTrace.Reset();

        public void CreateGUI()
        {
            using var _t0 = Core.PerfTrace.Begin("HMIWorkspaceWindow.CreateGUI(total)");

            VisualTreeAsset tree;
            StyleSheet style;
            using (Core.PerfTrace.Begin("  LoadAssets(UXML+USS)"))
            {
                tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.hmi.workspace/Editor/UXML/HMIWorkspace.uxml");
                style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.hmi.workspace/Editor/USS/HMIWorkspace.uss");
            }

            if (tree != null)
            {
                rootVisualElement.Clear();
                tree.CloneTree(rootVisualElement);
            }

            if (style != null && !rootVisualElement.styleSheets.Contains(style))
                rootVisualElement.styleSheets.Add(style);

            using (Core.PerfTrace.Begin("  InitializeArchitecture"))
                InitializeArchitecture();
            using (Core.PerfTrace.Begin("  BindViews"))
                BindViews();

            // Initialize 必须在 BindViews 之后调用：
            // Controller.Initialize() 会发布事件（如 FilteredAssetsChanged），
            // View 必须先完成订阅才能接收到初始数据。
            using (Core.PerfTrace.Begin("  WorkspaceController.Initialize"))
                _workspaceController.Initialize();
        }

        // 暴露给 View 层，供 BatchReplaceView 获取资产注册表
        private AssetRegistry _assetRegistry;
        private SceneController _sceneController;
        private VehicleSetupController _vehicleSetupController;
        private LogicController _logicController;

        private void InitializeArchitecture()
        {
            _state = new WorkspaceState();
            _assetRegistry = new AssetRegistry();
            var assetRegistry = _assetRegistry;
            var previewCache = new PreviewCache();
            var commandHistory = new CommandHistory();

            var assetService = new AssetService();
            _selectionService = new SelectionService();
            var selectionService = _selectionService;
            var undoService = new UndoService();
            var prefabService = new PrefabService();
            var previewService = new PreviewService();
            var packageService = new PackageService();
            var aiService = new AIService();

            var selectionController = new SelectionController(selectionService, _state, assetRegistry);
            var assetController = new AssetBrowserController(assetService, assetRegistry, _state);
            var previewController = new PreviewController(previewService, previewCache, _state);
            var actionController = new ActionController(undoService, prefabService, selectionService, _state, commandHistory);
            var dependencyController = new DependencyController(packageService, _state);
            var aiController = new AIController(aiService, _state);
            var sceneController = new SceneController(_state);
            _sceneController = sceneController;
            var vehicleSetupController = new VehicleSetupController(_state);
            _vehicleSetupController = vehicleSetupController;
            var logicController = new LogicController(_state);
            _logicController = logicController;

            _workspaceController = new WorkspaceController(_state);
            _workspaceController.AddChild(selectionController);
            _workspaceController.AddChild(assetController);
            _workspaceController.AddChild(previewController);
            _workspaceController.AddChild(actionController);
            _workspaceController.AddChild(dependencyController);
            _workspaceController.AddChild(aiController);
            _workspaceController.AddChild(sceneController);
            _workspaceController.AddChild(vehicleSetupController);
            _workspaceController.AddChild(logicController);

            rootVisualElement.userData = new WindowContext(_state, selectionController, assetController, actionController, aiController, sceneController, vehicleSetupController, logicController, commandHistory);
        }

        private void BindViews()
        {
            if (rootVisualElement.userData is not WindowContext ctx) return;

            // ── 首屏必需的 View：立即绑定 ──
            var topBar = new TopBarView(rootVisualElement.Q<VisualElement>("top-bar"));
            topBar.Bind(ctx.State, _workspaceController, ctx.AIController, ctx.AssetBrowserController);

            var sidebar = new SidebarView(rootVisualElement.Q<VisualElement>("sidebar-content"));
            sidebar.Bind(ctx.State, ctx.AssetBrowserController);

            var homeView = new HomeView(rootVisualElement.Q<VisualElement>("home-view"));
            homeView.Bind(ctx.State, ctx.CommandHistory);

            var assetGrid = new AssetGridView(rootVisualElement.Q<VisualElement>("asset-browser-view"));
            assetGrid.Bind(ctx.State, ctx.SelectionController, ctx.AssetBrowserController);

            var actionPanel = new InspectorPanelView(rootVisualElement.Q<VisualElement>("preview-tab-content"));
            actionPanel.Bind(ctx.State, ctx.ActionController);

            // 覆盖 UXML 中的标签文字以匹配新面板用途
            var previewTabBtn = rootVisualElement.Q<Button>("preview-tab-btn");
            if (previewTabBtn != null) previewTabBtn.text = "\u64CD\u4F5C";

            var aiContext = new AIContextView(rootVisualElement.Q<VisualElement>("ai-tab-content"));
            aiContext.Bind(ctx.State, ctx.AIController);

            var bottomBar = new BottomBarView(rootVisualElement.Q<VisualElement>("bottom-bar"));
            bottomBar.Bind(ctx.State, ctx.ActionController, ctx.CommandHistory);

            // ── 非首屏 View：延迟到首次切换时绑定（节省 ~15ms 初始化时间）──
            // ScenePreview, Compare, VehicleSetup, BatchReplace, SceneBuilder
            // 通过 EnsureLazyBind(mode) 在 UpdateCenterView 时按需绑定

            CachePanels();
            BuildCenterToolbar();
            BindShellInteractions(ctx.State);
            UpdateCenterView(ctx.State.CurrentViewMode.Value);
            SetRightTab(showPreview: true);
        }

        /// <summary>
        /// 按需绑定非首屏 View。首次切换到对应 ViewMode 时才执行 Bind，
        /// 避免窗口打开时创建和绑定用户暂不使用的重量级视图。
        /// </summary>
        private void EnsureLazyBind(ViewMode mode)
        {
            if (rootVisualElement.userData is not WindowContext ctx) return;

            switch (mode)
            {
                case ViewMode.Scene when !_scenePreviewBound:
                    _scenePreviewBound = true;
                    var scenePreview = new ScenePreviewView(rootVisualElement.Q<VisualElement>("scene-preview-view"));
                    scenePreview.Bind(ctx.State);
                    break;

                case ViewMode.Compare when !_compareBound:
                    _compareBound = true;
                    var compareView = new CompareView(rootVisualElement.Q<VisualElement>("compare-view"));
                    compareView.SetActionController(ctx.ActionController);
                    compareView.Bind(ctx.State);
                    break;

                case ViewMode.VehicleSetup when !_vehicleSetupBound:
                    _vehicleSetupBound = true;
                    _vehicleSetupInstance = new VehicleSetupView(rootVisualElement.Q<VisualElement>("vehicle-setup-view"), ctx.State);
                    _vehicleSetupInstance.Bind(ctx.VehicleSetupController);
                    break;

                case ViewMode.BatchReplace when !_batchReplaceBound:
                    _batchReplaceBound = true;
                    var batchReplace = new BatchReplaceView(rootVisualElement.Q<VisualElement>("batch-replace-view"), ctx.State, _assetRegistry);
                    batchReplace.Bind(ctx.ActionController);
                    break;

                case ViewMode.SceneBuilder when !_sceneBuilderBound:
                    _sceneBuilderBound = true;
                    var sceneBuilder = new SceneBuilderView(rootVisualElement.Q<VisualElement>("scene-builder-view"), ctx.State);
                    sceneBuilder.Bind(ctx.SceneController);
                    break;

                case ViewMode.LogicFlow when !_logicFlowBound:
                    _logicFlowBound = true;
                    var logicFlowView = new LogicFlowView();
                    logicFlowView.Bind(rootVisualElement.Q<VisualElement>("logic-flow-view"), ctx.State, ctx.LogicController);
                    break;
            }
        }

        private void OnDisable()
        {
            _vehicleSetupInstance?.Dispose();
            _workspaceController?.Dispose();   // 会递归 Dispose 所有子 Controller
            _selectionService?.Dispose();
        }

        private void CachePanels()
        {
            _homeView = rootVisualElement.Q<VisualElement>("home-view");
            _assetBrowserView = rootVisualElement.Q<VisualElement>("asset-browser-view");
            _scenePreviewView = rootVisualElement.Q<VisualElement>("scene-preview-view");
            _compareView = rootVisualElement.Q<VisualElement>("compare-view");
            _vehicleSetupView = rootVisualElement.Q<VisualElement>("vehicle-setup-view");
            _batchReplaceView = rootVisualElement.Q<VisualElement>("batch-replace-view");
            _sceneBuilderView = rootVisualElement.Q<VisualElement>("scene-builder-view");
            _logicFlowView = rootVisualElement.Q<VisualElement>("logic-flow-view");
            _previewTabContent = rootVisualElement.Q<VisualElement>("preview-tab-content");
            _aiTabContent = rootVisualElement.Q<VisualElement>("ai-tab-content");
            _previewTabButton = rootVisualElement.Q<Button>("preview-tab-btn");
            _aiTabButton = rootVisualElement.Q<Button>("ai-tab-btn");
        }

        private void BindShellInteractions(WorkspaceState state)
        {
            state.CurrentViewMode.Changed += (_, mode) =>
            {
                UpdateCenterView(mode);
                // 切换视图模式时发布清除事件，让右面板显示新模式的空状态
                var sourceMode = mode switch
                {
                    ViewMode.Home => "Home",
                    ViewMode.Grid => "AssetBrowser",
                    ViewMode.BatchReplace => "BatchReplace",
                    ViewMode.SceneBuilder => "SceneBuilder",
                    ViewMode.VehicleSetup => "VehicleSetup",
                    ViewMode.LogicFlow => "LogicFlow",
                    _ => mode.ToString(),
                };
                var emptyMsg = mode switch
                {
                    ViewMode.Home => "选择一个任务开始",
                    ViewMode.Grid => "从中间列表点击资产查看详情",
                    ViewMode.BatchReplace => "在 Hierarchy 中选中 GameObject 以开始批量替换",
                    ViewMode.SceneBuilder => "从左侧选择场景模板开始配置",
                    ViewMode.VehicleSetup => "导入 FBX 文件以开始车辆设置",
                    ViewMode.LogicFlow => "右键添加节点，连接构建交互逻辑",
                    _ => "选择内容以查看详情",
                };
                Core.SelectionEvents.ContextCleared.Publish(
                    new Core.SelectionContextClearedEvent(sourceMode, emptyMsg));
            };

            _previewTabButton?.RegisterCallback<ClickEvent>(_ => SetRightTab(showPreview: true));
            _aiTabButton?.RegisterCallback<ClickEvent>(_ => SetRightTab(showPreview: false));
        }

        private void BuildCenterToolbar()
        {
            var toolbar = rootVisualElement.Q<VisualElement>("center-toolbar");
            if (toolbar == null) return;

            toolbar.Clear();
            toolbar.Add(CreateToolbarChip(_state.CurrentSidebarTab.Value));
            toolbar.Add(CreateToolbarChip(_state.CurrentViewMode.Value.ToLabel()));
            _state.CurrentSidebarTab.Changed += (_, value) => RefreshToolbarChip(0, value);
            _state.CurrentViewMode.Changed += (_, value) => RefreshToolbarChip(1, value.ToLabel());
        }

        private void UpdateCenterView(ViewMode mode)
        {
            // 按需绑定非首屏 View（首次切换时才创建和绑定）
            EnsureLazyBind(mode);

            SetActive(_homeView, mode == ViewMode.Home);
            SetActive(_assetBrowserView, mode == ViewMode.Grid);
            SetActive(_scenePreviewView, mode == ViewMode.Scene);
            SetActive(_compareView, mode == ViewMode.Compare);
            SetActive(_vehicleSetupView, mode == ViewMode.VehicleSetup);
            SetActive(_batchReplaceView, mode == ViewMode.BatchReplace);
            SetActive(_sceneBuilderView, mode == ViewMode.SceneBuilder);
            SetActive(_logicFlowView, mode == ViewMode.LogicFlow);
        }

        private void SetRightTab(bool showPreview)
        {
            SetActive(_previewTabContent, showPreview);
            SetActive(_aiTabContent, !showPreview);
            _state.CurrentRightTab.Value = showPreview ? "操作" : "AI 助手";

            if (_previewTabButton != null)
            {
                if (showPreview) _previewTabButton.AddToClassList("active");
                else _previewTabButton.RemoveFromClassList("active");
            }

            if (_aiTabButton != null)
            {
                if (!showPreview) _aiTabButton.AddToClassList("active");
                else _aiTabButton.RemoveFromClassList("active");
            }
        }

        private static void SetActive(VisualElement element, bool active)
        {
            if (element == null) return;
            if (active) element.AddToClassList("active");
            else element.RemoveFromClassList("active");
        }

        private static Label CreateToolbarChip(string text)
        {
            var chip = new Label(text);
            chip.AddToClassList("toolbar-chip");
            return chip;
        }

        private void RefreshToolbarChip(int index, string text)
        {
            var toolbar = rootVisualElement.Q<VisualElement>("center-toolbar");
            if (toolbar == null || index < 0 || index >= toolbar.childCount) return;
            if (toolbar[index] is Label label)
                label.text = text;
        }

        /// <summary>
        /// 窗口上下文：持有 State 和各核心 Controller 的引用，供 View 层绑定使用
        /// </summary>
        private sealed class WindowContext
        {
            public WorkspaceState State { get; }
            public SelectionController SelectionController { get; }
            public AssetBrowserController AssetBrowserController { get; }
            public ActionController ActionController { get; }
            public AIController AIController { get; }
            public SceneController SceneController { get; }
            public VehicleSetupController VehicleSetupController { get; }
            public LogicController LogicController { get; }
            public CommandHistory CommandHistory { get; }

            public WindowContext(WorkspaceState state,
                SelectionController selectionController,
                AssetBrowserController assetBrowserController,
                ActionController actionController,
                AIController aiController,
                SceneController sceneController,
                VehicleSetupController vehicleSetupController,
                LogicController logicController,
                CommandHistory commandHistory)
            {
                State = state;
                SelectionController = selectionController;
                AssetBrowserController = assetBrowserController;
                ActionController = actionController;
                AIController = aiController;
                SceneController = sceneController;
                VehicleSetupController = vehicleSetupController;
                LogicController = logicController;
                CommandHistory = commandHistory;
            }
        }
    }
}
