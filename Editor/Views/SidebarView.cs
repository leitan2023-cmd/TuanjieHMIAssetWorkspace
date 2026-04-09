using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 侧边栏 — 一级导航 + 当前状态入口。
    ///
    /// 增强点（vs 旧版）：
    ///   1. 资产分类按钮带动态计数（材质 18 / 模型 12 / 特效 12）
    ///   2. 工具区与当前 ViewMode 联动高亮
    ///   3. 底部环境状态卡片（依赖摘要）
    ///   4. 首页按钮始终可见
    /// </summary>
    public sealed class SidebarView
    {
        private readonly VisualElement _root;
        private readonly List<Button> _categoryButtons = new();
        private readonly Dictionary<ViewMode, VisualElement> _toolEntries = new();
        private WorkspaceState _state;
        private AssetBrowserController _assetBrowserController;

        // 计数标签缓存
        private Label _countAll;
        private Label _countMaterial;
        private Label _countModel;
        private Label _countEffect;

        // 环境状态
        private Label _envStatusLabel;

        public SidebarView(VisualElement root) => _root = root;

        private bool _initialized;

        public void Bind(WorkspaceState state, AssetBrowserController assetBrowserController)
        {
            if (_root == null) return;
            _state = state;
            _assetBrowserController = assetBrowserController;

            RebuildSidebar();

            // 首次 FilteredChanged → 刷新计数
            AssetEvents.FilteredChanged.Subscribe(_ =>
            {
                if (!_initialized)
                {
                    _initialized = true;
                    RefreshCounts();
                }
            });

            // Pipeline / 资产变化 → 刷新计数
            state.TotalAssetCount.Changed += (_, __) => RefreshCounts();
            state.MaterialCount.Changed += (_, __) => RefreshCounts();
            state.ModelCount.Changed += (_, __) => RefreshCounts();
            state.EffectCount.Changed += (_, __) => RefreshCounts();

            // ViewMode 变化 → 高亮当前工具
            state.CurrentViewMode.Changed += (_, mode) => RefreshActiveMode(mode);

            // 依赖变化 → 刷新环境卡片
            state.EnvironmentReady.Changed += (_, __) => RefreshEnvStatus();
            state.CoreHealth.Changed += (_, __) => RefreshEnvStatus();

            // 初始状态
            RefreshActiveMode(state.CurrentViewMode.Value);
            RefreshEnvStatus();
        }

        private void RebuildSidebar()
        {
            if (_root == null) return;
            var state = _state;
            var abc = _assetBrowserController;

            _root.Clear();
            _categoryButtons.Clear();
            _toolEntries.Clear();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _root.Add(scroll);

            // ── 首页按钮 ──
            var homeBtn = new Button(() =>
            {
                state.CurrentViewMode.Value = ViewMode.Home;
            }) { text = "\u25C0  \u9996\u9875" };
            homeBtn.AddToClassList("sidebar-home-btn");
            scroll.Add(homeBtn);

            // ══ 资产分类 ══
            var assetSection = new VisualElement();
            assetSection.AddToClassList("sidebar-section");

            var assetTitle = new Label("\u8D44\u4EA7\u7C7B\u578B");
            assetTitle.AddToClassList("sidebar-section-title");
            assetSection.Add(assetTitle);

            var allResult = CreateCountNavButton("\u5168\u90E8", true, () =>
            {
                SetActiveCategoryButton("\u5168\u90E8");
                abc.SetCategoryFilter("");
                state.CurrentViewMode.Value = ViewMode.Grid;
            });
            _countAll = allResult.countLabel;
            assetSection.Add(allResult.row);

            var matResult = CreateCountNavButton("\u6750\u8D28\u5E93", false, () =>
            {
                SetActiveCategoryButton("\u6750\u8D28\u5E93");
                abc.SetCategoryFilter("\u6750\u8D28\u5E93");
                state.CurrentViewMode.Value = ViewMode.Grid;
            });
            _countMaterial = matResult.countLabel;
            assetSection.Add(matResult.row);

            var modResult = CreateCountNavButton("\u6A21\u578B\u5E93", false, () =>
            {
                SetActiveCategoryButton("\u6A21\u578B\u5E93");
                abc.SetCategoryFilter("\u6A21\u578B\u5E93");
                state.CurrentViewMode.Value = ViewMode.Grid;
            });
            _countModel = modResult.countLabel;
            assetSection.Add(modResult.row);

            var fxResult = CreateCountNavButton("\u7279\u6548\u5E93", false, () =>
            {
                SetActiveCategoryButton("\u7279\u6548\u5E93");
                abc.SetCategoryFilter("\u7279\u6548\u5E93");
                state.CurrentViewMode.Value = ViewMode.Grid;
            });
            _countEffect = fxResult.countLabel;
            assetSection.Add(fxResult.row);

            scroll.Add(assetSection);

            // ══ 工具 ══
            var toolSection = new VisualElement();
            toolSection.AddToClassList("sidebar-section");

            var toolTitle = new Label("\u5DE5\u5177");
            toolTitle.AddToClassList("sidebar-section-title");
            toolSection.Add(toolTitle);

            toolSection.Add(CreateToolEntry(ViewMode.VehicleSetup,
                "\u8F66\u8F86\u8BBE\u7F6E",
                "\u5BFC\u5165\u5E76\u914D\u7F6E\u8F66\u8F86\u96F6\u4EF6",
                () => state.CurrentViewMode.Value = ViewMode.VehicleSetup));

            toolSection.Add(CreateToolEntry(ViewMode.BatchReplace,
                "\u6279\u91CF\u66FF\u6362",
                "\u9009\u62E9\u76EE\u6807\u5E76\u6279\u91CF\u66FF\u6362\u6750\u8D28",
                () => state.CurrentViewMode.Value = ViewMode.BatchReplace));

            toolSection.Add(CreateToolEntry(ViewMode.SceneBuilder,
                "\u573A\u666F\u6784\u5EFA",
                "\u9009\u6A21\u677F\u3001\u914D\u73AF\u5883\u5E76\u751F\u6210\u573A\u666F",
                () => state.CurrentViewMode.Value = ViewMode.SceneBuilder));

            toolSection.Add(CreateToolEntry(ViewMode.LogicFlow,
                "\u903B\u8F91\u7F16\u8F91",
                "\u53EF\u89C6\u5316\u6784\u5EFA\u4EA4\u4E92\u903B\u8F91\u6D41",
                () => state.CurrentViewMode.Value = ViewMode.LogicFlow));

            scroll.Add(toolSection);

            // ══ 环境状态卡片 ══
            var envCard = new VisualElement();
            envCard.AddToClassList("sidebar-info-card");

            var envTitle = new Label("\u73AF\u5883\u72B6\u6001");
            envTitle.AddToClassList("sidebar-info-title");
            envCard.Add(envTitle);

            _envStatusLabel = new Label("\u68C0\u6D4B\u4E2D\u2026");
            _envStatusLabel.AddToClassList("sidebar-info-body");
            envCard.Add(_envStatusLabel);

            scroll.Add(envCard);
        }

        // ═══════════════════════════════════════════════════════════
        // 刷新逻辑
        // ═══════════════════════════════════════════════════════════

        private void RefreshCounts()
        {
            if (_state == null) return;
            int total = _state.TotalAssetCount.Value;
            int mat = _state.MaterialCount.Value;
            int mod = _state.ModelCount.Value;
            int fx  = _state.EffectCount.Value;

            if (_countAll != null) _countAll.text = total.ToString();
            if (_countMaterial != null) _countMaterial.text = mat.ToString();
            if (_countModel != null) _countModel.text = mod.ToString();
            if (_countEffect != null) _countEffect.text = fx.ToString();
        }

        private void RefreshActiveMode(ViewMode mode)
        {
            foreach (var kvp in _toolEntries)
            {
                if (kvp.Key == mode)
                    kvp.Value.AddToClassList("sidebar-tool-active");
                else
                    kvp.Value.RemoveFromClassList("sidebar-tool-active");
            }

            // Grid 模式时，资产分类按钮保持上次选中；其他模式清除高亮
            if (mode != ViewMode.Grid)
            {
                foreach (var btn in _categoryButtons)
                    btn.RemoveFromClassList("active");
            }
        }

        private void RefreshEnvStatus()
        {
            if (_envStatusLabel == null || _state == null) return;

            bool envReady = _state.EnvironmentReady.Value;
            if (envReady)
            {
                _envStatusLabel.text = "\u2713 HMIRP \u56DB\u5305\u5168\u90E8\u5C31\u7EEA\n\u6240\u6709\u529F\u80FD\u53EF\u7528";
            }
            else
            {
                var missing = new List<string>();
                if (_state.CoreHealth.Value != PackageHealth.Installed) missing.Add("Core");
                if (_state.ShaderLibraryHealth.Value != PackageHealth.Installed) missing.Add("Shader");
                if (_state.MaterialLibraryHealth.Value != PackageHealth.Installed) missing.Add("Material");
                if (_state.StateRenderHealth.Value != PackageHealth.Installed) missing.Add("StateRender");

                _envStatusLabel.text = $"\u26A0 \u7F3A\u5931\uFF1A{string.Join(", ", missing)}\n\u90E8\u5206\u529F\u80FD\u964D\u7EA7";
            }
        }

        // ═══════════════════════════════════════════════════════════
        // UI 构建辅助
        // ═══════════════════════════════════════════════════════════

        private (Label countLabel, VisualElement row) CreateCountNavButton(
            string text, bool active, Action onClick)
        {
            var row = new VisualElement();
            row.AddToClassList("sidebar-nav-row");

            var button = new Button(onClick) { text = text, name = text };
            button.AddToClassList("sidebar-button");
            if (active) button.AddToClassList("active");
            _categoryButtons.Add(button);

            var count = new Label("0");
            count.AddToClassList("sidebar-count");

            row.Add(button);
            row.Add(count);
            return (count, row);
        }

        private VisualElement CreateToolEntry(ViewMode mode, string title, string desc, Action onClick)
        {
            var card = new VisualElement();
            card.AddToClassList("tool-card");

            var button = new Button(onClick) { text = title };
            button.AddToClassList("tool-title");
            var descLabel = new Label(desc);
            descLabel.AddToClassList("tool-desc");

            card.Add(button);
            card.Add(descLabel);

            _toolEntries[mode] = card;
            return card;
        }

        private void SetActiveCategoryButton(string buttonName)
        {
            foreach (var button in _categoryButtons)
            {
                if (button.name == buttonName)
                    button.AddToClassList("active");
                else
                    button.RemoveFromClassList("active");
            }
        }
    }
}
