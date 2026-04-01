using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    public sealed class HMIWorkspaceWindow : EditorWindow
    {
        private WorkspaceState _state;
        private WorkspaceController _workspaceController;

        [MenuItem("Window/HMI Asset Workspace")]
        public static void Open()
        {
            var window = GetWindow<HMIWorkspaceWindow>();
            window.titleContent = new GUIContent("HMI Workspace");
            window.minSize = new UnityEngine.Vector2(1200, 700);
        }

        public void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.hmi.workspace/Editor/UXML/HMIWorkspace.uxml");
            if (tree != null)
            {
                rootVisualElement.Clear();
                tree.CloneTree(rootVisualElement);
            }

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.hmi.workspace/Editor/USS/HMIWorkspace.uss");
            if (style != null && !rootVisualElement.styleSheets.Contains(style))
                rootVisualElement.styleSheets.Add(style);

            InitializeArchitecture();
            BindViews();
        }

        private void InitializeArchitecture()
        {
            _state = new WorkspaceState();
            var assetRegistry = new AssetRegistry();
            var previewCache = new PreviewCache();

            var assetService = new AssetService();
            var selectionService = new SelectionService();
            var undoService = new UndoService();
            var prefabService = new PrefabService();
            var previewService = new PreviewService();
            var packageService = new PackageService();
            var aiService = new AIService();

            var selectionController = new SelectionController(selectionService, _state, assetRegistry);
            var assetController = new AssetBrowserController(assetService, assetRegistry, _state);
            var previewController = new PreviewController(previewService, previewCache, _state);
            var actionController = new ActionController(undoService, prefabService, selectionService, _state);
            var dependencyController = new DependencyController(packageService, _state);
            var aiController = new AIController(aiService, _state);
            var sceneController = new SceneController(_state);

            _workspaceController = new WorkspaceController(_state);
            _workspaceController.AddChild(selectionController);
            _workspaceController.AddChild(assetController);
            _workspaceController.AddChild(previewController);
            _workspaceController.AddChild(actionController);
            _workspaceController.AddChild(dependencyController);
            _workspaceController.AddChild(aiController);
            _workspaceController.AddChild(sceneController);
            _workspaceController.Initialize();

            rootVisualElement.userData = new WindowContext(_state, selectionController, assetController, actionController, aiController);
        }

        private void BindViews()
        {
            if (rootVisualElement.userData is not WindowContext ctx) return;

            var topBar = new TopBarView(rootVisualElement.Q<VisualElement>("top-bar"));
            topBar.Bind(ctx.State, ctx.AIController, ctx.AssetBrowserController);

            var assetGrid = new AssetGridView(rootVisualElement.Q<VisualElement>("center-panel"));
            assetGrid.Bind(ctx.State, ctx.SelectionController);

            var inspector = new InspectorPanelView(rootVisualElement.Q<VisualElement>("right-panel"));
            inspector.Bind(ctx.State, ctx.ActionController);

            var bottomBar = new BottomBarView(rootVisualElement.Q<VisualElement>("bottom-bar"));
            bottomBar.Bind(ctx.State);
        }

        private void OnDisable()
        {
            _workspaceController?.Dispose();
        }

        /// <summary>
        /// 窗口上下文：持有 State 和各核心 Controller 的引用，供 View 层绑定使用
        /// 使用 class 替代 record 以兼容 Unity 2022.3（缺少 IsExternalInit）
        /// </summary>
        private sealed class WindowContext
        {
            public WorkspaceState State { get; }
            public SelectionController SelectionController { get; }
            public AssetBrowserController AssetBrowserController { get; }
            public ActionController ActionController { get; }
            public AIController AIController { get; }

            public WindowContext(WorkspaceState state,
                SelectionController selectionController,
                AssetBrowserController assetBrowserController,
                ActionController actionController,
                AIController aiController)
            {
                State = state;
                SelectionController = selectionController;
                AssetBrowserController = assetBrowserController;
                ActionController = actionController;
                AIController = aiController;
            }
        }
    }
}
