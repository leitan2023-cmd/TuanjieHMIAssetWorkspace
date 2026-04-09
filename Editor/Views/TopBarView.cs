using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// TopBar — 当前工作上下文条。
    ///
    /// 增强点（vs 旧版）：
    ///   1. 当前 ViewMode 标签（面包屑式）
    ///   2. 搜索栏与 SearchKeyword/FilteredAssetCount 联动
    ///   3. 轻量上下文统计（已筛选/总计）
    ///   4. 依赖状态点指示
    /// </summary>
    public sealed class TopBarView : ITopBarView
    {
        private readonly VisualElement _root;
        private Button _gridButton;
        private Button _listButton;

        // 新增：上下文元素
        private Label _modeLabel;
        private Label _resultCountLabel;
        private Label _envDot;

        public TopBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, WorkspaceController workspaceController,
            AIController aiController, AssetBrowserController assetBrowserController)
        {
            if (_root == null) return;
            var selectionLabel = _root.Q<Label>("selection-label");
            var sceneLabel = _root.Q<Label>("scene-label");
            var pipelineLabel = _root.Q<Label>("pipeline-label");
            var searchField = _root.Q<ToolbarSearchField>("search-input");
            _gridButton = _root.Q<Button>("view-grid-btn");
            _listButton = _root.Q<Button>("view-list-btn");

            // ── 注入新的上下文元素 ──
            InjectContextElements(state);

            if (searchField != null)
                searchField.tooltip = "\u6309\u540D\u79F0\u3001\u5206\u7C7B\u6216\u6807\u7B7E\u7B5B\u9009\u8D44\u4EA7";

            state.UnitySelection.BindToLabel(selectionLabel,
                obj => obj != null ? obj.name : "(\u65E0)");

            state.ActiveScene.BindToLabel(sceneLabel,
                s => string.IsNullOrEmpty(s.Name) ? "\u672A\u547D\u540D" : s.Name);

            state.PipelineName.BindToLabel(pipelineLabel);

            // ── 搜索联动 ──
            searchField?.RegisterValueChangedCallback(evt =>
                assetBrowserController.ApplySearch(evt.newValue));

            // 如果 SearchKeyword 被外部改变，同步到搜索框
            state.SearchKeyword.Changed += (_, kw) =>
            {
                if (searchField != null && searchField.value != kw)
                    searchField.SetValueWithoutNotify(kw);
            };

            // ── 结果计数 ──
            state.FilteredAssetCount.Changed += (_, count) => RefreshResultCount(state);
            state.TotalAssetCount.Changed += (_, _) => RefreshResultCount(state);

            // ── ViewMode ──
            state.CurrentViewMode.Changed += (_, mode) =>
            {
                RefreshModeLabel(mode);

                // 非 Grid 模式时，隐藏 grid/list toggle + 搜索结果计数
                var group = _root.Q<VisualElement>(className: "view-mode-group");
                bool isGrid = mode == ViewMode.Grid;
                if (group != null)
                    group.style.display = isGrid ? DisplayStyle.Flex : DisplayStyle.None;

                // 搜索栏：Grid 模式显示结果计数，其他模式隐藏
                if (_resultCountLabel != null)
                    _resultCountLabel.style.display = isGrid ? DisplayStyle.Flex : DisplayStyle.None;
            };

            // ── 环境状态指示点 ──
            state.EnvironmentReady.Changed += (_, ready) => RefreshEnvDot(ready);

            // ── 视图模式按钮 ──
            _gridButton?.RegisterCallback<ClickEvent>(_ =>
            {
                workspaceController.SetViewMode(ViewMode.Grid);
                SetGridListActive(isGrid: true);
            });

            _listButton?.RegisterCallback<ClickEvent>(_ =>
            {
                workspaceController.SetViewMode(ViewMode.Grid);
                SetGridListActive(isGrid: false);
                ListModeRequested?.Invoke();
            });

            // ── 初始状态 ──
            SetGridListActive(isGrid: true);
            RefreshModeLabel(state.CurrentViewMode.Value);
            RefreshResultCount(state);
            RefreshEnvDot(state.EnvironmentReady.Value);
        }

        /// <summary>列表模式请求事件，AssetGridView 可订阅。</summary>
        public event System.Action ListModeRequested;

        // ═══════════════════════════════════════════════════════════
        // 上下文元素注入
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 在 UXML 已有结构中插入新的上下文元素：
        ///   左侧：ViewMode 标签 + 环境点
        ///   中央搜索栏右侧：结果计数
        /// </summary>
        private void InjectContextElements(WorkspaceState state)
        {
            // ── ViewMode 标签 → 插入到 left cluster ──
            var leftCluster = _root.Q<VisualElement>(className: "top-cluster-left");
            if (leftCluster != null)
            {
                // 环境状态点
                _envDot = new Label("\u25CF");
                _envDot.AddToClassList("top-env-dot");
                leftCluster.Add(_envDot);

                // 当前模式标签
                _modeLabel = new Label("\u9996\u9875");
                _modeLabel.AddToClassList("top-mode-label");
                leftCluster.Add(_modeLabel);
            }

            // ── 结果计数 → 插入到 center cluster ──
            var centerCluster = _root.Q<VisualElement>(className: "top-cluster-center");
            if (centerCluster != null)
            {
                _resultCountLabel = new Label("");
                _resultCountLabel.AddToClassList("top-result-count");
                centerCluster.Add(_resultCountLabel);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 刷新逻辑
        // ═══════════════════════════════════════════════════════════

        private void RefreshModeLabel(ViewMode mode)
        {
            if (_modeLabel == null) return;
            _modeLabel.text = mode.ToLabel();

            // 切换颜色主题
            _modeLabel.RemoveFromClassList("top-mode-home");
            _modeLabel.RemoveFromClassList("top-mode-grid");
            _modeLabel.RemoveFromClassList("top-mode-tool");

            switch (mode)
            {
                case ViewMode.Home:
                    _modeLabel.AddToClassList("top-mode-home");
                    break;
                case ViewMode.Grid:
                    _modeLabel.AddToClassList("top-mode-grid");
                    break;
                default:
                    _modeLabel.AddToClassList("top-mode-tool");
                    break;
            }
        }

        private void RefreshResultCount(WorkspaceState state)
        {
            if (_resultCountLabel == null) return;
            int filtered = state.FilteredAssetCount.Value;
            int total = state.TotalAssetCount.Value;

            if (total == 0)
            {
                _resultCountLabel.text = "";
            }
            else if (filtered == total)
            {
                _resultCountLabel.text = $"{total} \u4E2A\u8D44\u4EA7";
            }
            else
            {
                _resultCountLabel.text = $"{filtered}/{total}";
            }
        }

        private void RefreshEnvDot(bool ready)
        {
            if (_envDot == null) return;
            _envDot.RemoveFromClassList("top-env-ok");
            _envDot.RemoveFromClassList("top-env-warn");
            _envDot.AddToClassList(ready ? "top-env-ok" : "top-env-warn");
            _envDot.tooltip = ready
                ? "HMIRP \u73AF\u5883\u5C31\u7EEA"
                : "\u90E8\u5206 HMIRP \u4F9D\u8D56\u7F3A\u5931\uFF0C\u90E8\u5206\u529F\u80FD\u4E0D\u53EF\u7528";
        }

        private void SetGridListActive(bool isGrid)
        {
            SetButtonState(_gridButton, isGrid);
            SetButtonState(_listButton, !isGrid);
        }

        // ── ITopBarView 接口实现 ──

        public void ShowAutocompleteDropdown(List<string> suggestions) { }
        public void HideAutocompleteDropdown() { }
        public void SetCommandText(string text) { }

        private static void SetButtonState(Button button, bool active)
        {
            if (button == null) return;
            if (active) button.AddToClassList("active");
            else button.RemoveFromClassList("active");
        }
    }
}
