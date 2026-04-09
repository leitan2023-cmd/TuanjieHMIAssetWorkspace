using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.LogicFlowEditor
{
    /// <summary>
    /// Logic Flow Editor 的独立 EditorWindow。
    /// 可通过双击 LogicGraph 资产打开，也可从 HMI Workspace 内部嵌入。
    /// </summary>
    public class LogicGraphEditorWindow : EditorWindow
    {
        private LogicGraphView _graphView;
        private LogicGraph _currentGraph;
        private Label _titleLabel;
        private bool _hasUnsavedChanges;

        public static void OpenWindow()
        {
            var window = GetWindow<LogicGraphEditorWindow>();
            window.titleContent = new GUIContent("Logic Flow Editor", EditorGUIUtility.IconContent("d_AnimatorController Icon").image);
            window.minSize = new Vector2(600, 400);
        }

        /// <summary>
        /// 打开并加载指定的 LogicGraph 资产。
        /// </summary>
        public static void OpenWithGraph(LogicGraph graph)
        {
            var window = GetWindow<LogicGraphEditorWindow>();
            window.titleContent = new GUIContent("Logic Flow Editor");
            window.LoadGraph(graph);
        }

        /// <summary>
        /// 双击 LogicGraph 资产时自动打开。
        /// </summary>
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceID) as LogicGraph;
            if (asset != null)
            {
                OpenWithGraph(asset);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void OnDisable()
        {
            if (_hasUnsavedChanges && _currentGraph != null)
            {
                if (EditorUtility.DisplayDialog("未保存的更改",
                    $"Logic Graph \"{_currentGraph.name}\" 有未保存的更改，是否保存？",
                    "保存", "放弃"))
                {
                    SaveGraph();
                }
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            // ── 工具栏 ──
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.height = 28;
            toolbar.style.backgroundColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));

            _titleLabel = new Label("Logic Flow Editor — 未加载");
            _titleLabel.style.flexGrow = 1;
            _titleLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.85f));
            _titleLabel.style.fontSize = 12;
            toolbar.Add(_titleLabel);

            var newBtn = new Button(CreateNewGraph) { text = "新建" };
            newBtn.style.marginRight = 4;
            toolbar.Add(newBtn);

            var loadBtn = new Button(LoadGraphFromDialog) { text = "加载" };
            loadBtn.style.marginRight = 4;
            toolbar.Add(loadBtn);

            var saveBtn = new Button(SaveGraph) { text = "保存" };
            saveBtn.style.marginRight = 4;
            toolbar.Add(saveBtn);

            var executeBtn = new Button(ExecuteInEditor) { text = "▶ 测试执行" };
            executeBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.5f, 0.3f));
            executeBtn.style.color = new StyleColor(Color.white);
            toolbar.Add(executeBtn);

            rootVisualElement.Add(toolbar);

            // ── GraphView ──
            _graphView = new LogicGraphView();
            _graphView.StretchToParentSize();
            _graphView.style.flexGrow = 1;
            _graphView.OnGraphChanged = () => _hasUnsavedChanges = true;
            rootVisualElement.Add(_graphView);

            // 恢复之前打开的图
            if (_currentGraph != null)
                _graphView.LoadGraph(_currentGraph);
        }

        public void LoadGraph(LogicGraph graph)
        {
            _currentGraph = graph;
            _hasUnsavedChanges = false;

            if (_graphView != null)
                _graphView.LoadGraph(graph);

            if (_titleLabel != null)
                _titleLabel.text = graph != null
                    ? $"Logic Flow Editor — {graph.name}"
                    : "Logic Flow Editor — 未加载";
        }

        private void CreateNewGraph()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "创建 Logic Graph", "NewLogicGraph", "asset", "选择保存位置");

            if (string.IsNullOrEmpty(path)) return;

            var graph = ScriptableObject.CreateInstance<LogicGraph>();
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();

            LoadGraph(graph);
        }

        private void LoadGraphFromDialog()
        {
            var path = EditorUtility.OpenFilePanel("选择 Logic Graph", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            // 转换为相对路径
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            var graph = AssetDatabase.LoadAssetAtPath<LogicGraph>(path);
            if (graph != null)
                LoadGraph(graph);
            else
                EditorUtility.DisplayDialog("错误", "所选文件不是有效的 LogicGraph 资产", "确定");
        }

        private void SaveGraph()
        {
            if (_graphView != null && _currentGraph != null)
            {
                _graphView.SaveGraph();
                _hasUnsavedChanges = false;
                AssetDatabase.SaveAssets();
                Debug.Log($"[Logic Flow Editor] 已保存: {_currentGraph.name}");
            }
        }

        private void ExecuteInEditor()
        {
            if (_currentGraph == null)
            {
                EditorUtility.DisplayDialog("提示", "请先加载一个 Logic Graph", "确定");
                return;
            }

            // 先保存
            SaveGraph();

            // 在场景中查找或创建 LogicExecutor
            var executor = FindObjectOfType<LogicExecutor>();
            if (executor == null)
            {
                var go = new GameObject("_LogicExecutor (测试)");
                executor = go.AddComponent<LogicExecutor>();
                Undo.RegisterCreatedObjectUndo(go, "Create LogicExecutor");
            }

            executor.Graph = _currentGraph;
            executor.Initialize();
            executor.ExecuteTrigger("OnLoadTrigger");

            Debug.Log("[Logic Flow Editor] 测试执行完成 — 已触发 OnLoad");
        }

        /// <summary>
        /// 获取当前 GraphView 实例（供 HMIWorkspaceWindow 嵌入使用）。
        /// </summary>
        public LogicGraphView GetGraphView() => _graphView;
    }
}
