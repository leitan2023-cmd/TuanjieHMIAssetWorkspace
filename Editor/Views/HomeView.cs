using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 工作台首页 — 不再只是四个等权入口，而是告诉用户"现在最该做什么"。
    ///
    /// 布局：
    ///   ① 依赖警告横幅（沿用原有）
    ///   ② 项目状态摘要行
    ///   ③ 推荐主任务（动态计算，突出显示）+ 其他工具入口
    ///   ④ 最近操作日志（来自 CommandHistory）
    /// </summary>
    public sealed class HomeView
    {
        private readonly VisualElement _root;

        // 缓存卡片引用
        private VisualElement _browseCard;
        private VisualElement _sceneCard;
        private VisualElement _replaceCard;
        private VisualElement _vehicleCard;
        private VisualElement _bannerContainer;

        // 工作台特有
        private VisualElement _statusBar;
        private Label _assetCountLabel;
        private Label _depSummaryLabel;
        private Label _capabilityLabel;

        private VisualElement _primaryCard;
        private Label _primaryTitle;
        private Label _primaryDesc;
        private Label _primaryHint;
        private Action _primaryAction;

        private VisualElement _secondaryGrid;
        private VisualElement _recentList;
        private Label _recentEmpty;

        private WorkspaceState _state;
        private CommandHistory _commandHistory;
        private bool _recentListDirty;

        public HomeView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, CommandHistory commandHistory)
        {
            if (_root == null) return;
            _state = state;
            _commandHistory = commandHistory;
            _root.Clear();

            var container = new VisualElement();
            container.AddToClassList("home-container");

            // ── ① 依赖状态横幅 ──
            _bannerContainer = new VisualElement();
            _bannerContainer.AddToClassList("home-banner-container");
            container.Add(_bannerContainer);

            // ── ② 项目状态摘要行 ──
            BuildStatusBar(container);

            // ── ③ 推荐任务区域 ──
            BuildRecommendationArea(container, state);

            // ── ④ 最近操作 ──
            BuildRecentSection(container);

            // ── 底部提示 ──
            var hint = new Label("你也可以随时通过左侧「工具」栏切换任务");
            hint.AddToClassList("home-hint");
            container.Add(hint);

            _root.Add(container);

            // ── 响应式绑定 ──
            state.DependencyReport.Changed += (_, __) => RefreshDependencyUI(state);
            state.TotalAssetCount.Changed += (_, __) => RefreshStatusAndRecommendation();
            state.EnvironmentReady.Changed += (_, __) => RefreshStatusAndRecommendation();
            state.CoreHealth.Changed += (_, __) => RefreshStatusAndRecommendation();
            state.MaterialLibraryHealth.Changed += (_, __) => RefreshStatusAndRecommendation();

            // 操作事件 → 仅在首页可见时刷新最近操作列表
            ActionEvents.Executed.Subscribe(_ =>
            {
                if (_state.CurrentViewMode.Value == ViewMode.Home) RefreshRecentList();
                else _recentListDirty = true;
            });
            ActionEvents.Failed.Subscribe(_ =>
            {
                if (_state.CurrentViewMode.Value == ViewMode.Home) RefreshRecentList();
                else _recentListDirty = true;
            });

            // 切换回首页时，刷新脏数据
            state.CurrentViewMode.Changed += (_, mode) =>
            {
                if (mode == ViewMode.Home && _recentListDirty)
                {
                    _recentListDirty = false;
                    RefreshRecentList();
                    RefreshStatusAndRecommendation();
                }
            };

            // 初始刷新
            RefreshDependencyUI(state);
            RefreshStatusAndRecommendation();
            RefreshRecentList();
        }

        // 兼容旧签名（无 CommandHistory）
        public void Bind(WorkspaceState state) => Bind(state, null);

        // ═══════════════════════════════════════════════════════════
        // ② 项目状态摘要行
        // ═══════════════════════════════════════════════════════════

        private void BuildStatusBar(VisualElement parent)
        {
            _statusBar = new VisualElement();
            _statusBar.AddToClassList("home-status-bar");

            var titleLabel = new Label("HMI Asset Studio");
            titleLabel.AddToClassList("home-title");
            _statusBar.Add(titleLabel);

            var statsRow = new VisualElement();
            statsRow.AddToClassList("home-stats-row");

            _assetCountLabel = new Label("0 个资产");
            _assetCountLabel.AddToClassList("home-stat-chip");
            statsRow.Add(_assetCountLabel);

            _depSummaryLabel = new Label("检测中…");
            _depSummaryLabel.AddToClassList("home-stat-chip");
            statsRow.Add(_depSummaryLabel);

            _capabilityLabel = new Label("");
            _capabilityLabel.AddToClassList("home-stat-chip");
            _capabilityLabel.AddToClassList("home-stat-capability");
            statsRow.Add(_capabilityLabel);

            _statusBar.Add(statsRow);
            parent.Add(_statusBar);
        }

        // ═══════════════════════════════════════════════════════════
        // ③ 推荐任务 + 其他入口
        // ═══════════════════════════════════════════════════════════

        private void BuildRecommendationArea(VisualElement parent, WorkspaceState state)
        {
            var area = new VisualElement();
            area.AddToClassList("home-rec-area");

            // ── 主推荐卡片（大尺寸） ──
            _primaryCard = new VisualElement();
            _primaryCard.AddToClassList("home-primary-card");
            _primaryCard.RegisterCallback<ClickEvent>(_ => _primaryAction?.Invoke());

            var primaryTag = new Label("推荐下一步");
            primaryTag.AddToClassList("home-primary-tag");
            _primaryCard.Add(primaryTag);

            _primaryTitle = new Label("");
            _primaryTitle.AddToClassList("home-primary-title");
            _primaryCard.Add(_primaryTitle);

            _primaryDesc = new Label("");
            _primaryDesc.AddToClassList("home-primary-desc");
            _primaryCard.Add(_primaryDesc);

            _primaryHint = new Label("");
            _primaryHint.AddToClassList("home-primary-hint");
            _primaryCard.Add(_primaryHint);

            var goLabel = new Label("→  开始");
            goLabel.AddToClassList("home-primary-go");
            _primaryCard.Add(goLabel);

            area.Add(_primaryCard);

            // ── 其他任务（纵排小卡片） ──
            _secondaryGrid = new VisualElement();
            _secondaryGrid.AddToClassList("home-secondary-grid");

            _browseCard = CreateCompactCard(
                "◦", "home-icon-browse",
                "资产浏览",
                "浏览和管理材质、模型、特效",
                () => state.CurrentViewMode.Value = ViewMode.Grid);
            _secondaryGrid.Add(_browseCard);

            _sceneCard = CreateCompactCard(
                "○", "home-icon-scene",
                "场景搭建",
                "选模板、配灯光与环境，一键生成",
                () => state.CurrentViewMode.Value = ViewMode.SceneBuilder);
            _secondaryGrid.Add(_sceneCard);

            _replaceCard = CreateCompactCard(
                "↻", "home-icon-replace",
                "批量替换",
                "选目标、预览并批量替换材质",
                () => state.CurrentViewMode.Value = ViewMode.BatchReplace);
            _secondaryGrid.Add(_replaceCard);

            _vehicleCard = CreateCompactCard(
                "▲", "home-icon-vehicle",
                "车模配置",
                "导入 FBX，解析零件，导出 Schema",
                () => state.CurrentViewMode.Value = ViewMode.VehicleSetup);
            _secondaryGrid.Add(_vehicleCard);

            area.Add(_secondaryGrid);
            parent.Add(area);
        }

        // ═══════════════════════════════════════════════════════════
        // ④ 最近操作
        // ═══════════════════════════════════════════════════════════

        private void BuildRecentSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("home-recent-section");

            var header = new Label("最近操作");
            header.AddToClassList("home-recent-header");
            section.Add(header);

            _recentList = new VisualElement();
            _recentList.AddToClassList("home-recent-list");

            _recentEmpty = new Label("暂无操作记录");
            _recentEmpty.AddToClassList("home-recent-empty");
            _recentList.Add(_recentEmpty);

            section.Add(_recentList);
            parent.Add(section);
        }

        // ═══════════════════════════════════════════════════════════
        // 刷新逻辑
        // ═══════════════════════════════════════════════════════════

        private void RefreshStatusAndRecommendation()
        {
            if (_state == null) return;

            // ── 资产计数 ──
            int total = _state.TotalAssetCount.Value;
            if (_assetCountLabel != null)
                _assetCountLabel.text = total > 0
                    ? $"{total} 个资产已加载"
                    : "未扫描资产";

            // ── 依赖摘要 ──
            if (_depSummaryLabel != null)
            {
                bool envReady = _state.EnvironmentReady.Value;
                _depSummaryLabel.text = envReady
                    ? "✓ 环境就绪"
                    : "⚠ 部分依赖缺失";
                _depSummaryLabel.RemoveFromClassList("home-stat-ok");
                _depSummaryLabel.RemoveFromClassList("home-stat-warn");
                _depSummaryLabel.AddToClassList(envReady ? "home-stat-ok" : "home-stat-warn");
            }

            // ── 可用能力 ──
            if (_capabilityLabel != null)
            {
                int capabilities = 0;
                if (_state.MaterialLibraryHealth.Value == PackageHealth.Installed) capabilities++;
                if (_state.CoreHealth.Value == PackageHealth.Installed) capabilities++;
                if (_state.ShaderLibraryHealth.Value == PackageHealth.Installed) capabilities++;
                if (_state.StateRenderHealth.Value == PackageHealth.Installed) capabilities++;
                _capabilityLabel.text = $"{capabilities}/4 能力可用";
            }

            // ── 推荐主任务 ──
            ComputeRecommendation();

            // ── 卡片可用性 ──
            RefreshCardAvailability();
        }

        private void ComputeRecommendation()
        {
            if (_state == null || _primaryTitle == null) return;

            int totalAssets = _state.TotalAssetCount.Value;
            bool coreOk = _state.CoreHealth.Value == PackageHealth.Installed;
            bool matLibOk = _state.MaterialLibraryHealth.Value == PackageHealth.Installed;

            if (totalAssets == 0 && matLibOk)
            {
                SetPrimary(
                    "资产浏览",
                    "浏览工作区已加载的材质、模型和特效资产",
                    "开始浏览资产库，了解当前可用资源",
                    () => _state.CurrentViewMode.Value = ViewMode.Grid);
            }
            else if (totalAssets == 0)
            {
                SetPrimary(
                    "车模配置",
                    "导入 FBX 车辆模型，解析零件并生成 Schema",
                    "车模配置不依赖渲染管线，可立即开始",
                    () => _state.CurrentViewMode.Value = ViewMode.VehicleSetup);
            }
            else if (coreOk)
            {
                SetPrimary(
                    "场景搭建",
                    "选择场景模板，配置灯光、天气与环境，一键生成",
                    $"已有 {totalAssets} 个资产就绪，可以开始构建场景",
                    () => _state.CurrentViewMode.Value = ViewMode.SceneBuilder);
            }
            else
            {
                SetPrimary(
                    "资产浏览",
                    "查看已加载的 " + totalAssets + " 个资产",
                    "浏览材质、模型和特效库",
                    () => _state.CurrentViewMode.Value = ViewMode.Grid);
            }
        }

        private void SetPrimary(string title, string desc, string hint, Action action)
        {
            if (_primaryTitle != null) _primaryTitle.text = title;
            if (_primaryDesc != null) _primaryDesc.text = desc;
            if (_primaryHint != null) _primaryHint.text = hint;
            _primaryAction = action;
        }

        private void RefreshCardAvailability()
        {
            if (_state == null) return;

            var coreHealth     = _state.CoreHealth.Value;
            var shaderHealth   = _state.ShaderLibraryHealth.Value;
            var materialHealth = _state.MaterialLibraryHealth.Value;

            bool canBrowse  = materialHealth == PackageHealth.Installed;
            bool canScene   = coreHealth == PackageHealth.Installed;
            bool canReplace = coreHealth == PackageHealth.Installed
                              && shaderHealth == PackageHealth.Installed;

            SetCardEnabled(_browseCard,  canBrowse,  "需安装 Material Library");
            SetCardEnabled(_sceneCard,   canScene,   "需安装 HMIRP Core");
            SetCardEnabled(_replaceCard, canReplace,  "需安装 Core + Shader Library");
            SetCardEnabled(_vehicleCard, true, null);
        }

        private void RefreshRecentList()
        {
            if (_recentList == null) return;
            _recentList.Clear();

            if (_commandHistory == null || _commandHistory.Records.Count == 0)
            {
                _recentEmpty = new Label("暂无操作记录");
                _recentEmpty.AddToClassList("home-recent-empty");
                _recentList.Add(_recentEmpty);
                return;
            }

            // 最近 5 条操作
            int start = System.Math.Max(0, _commandHistory.Records.Count - 5);
            for (int i = _commandHistory.Records.Count - 1; i >= start; i--)
            {
                var record = _commandHistory.Records[i];
                var row = new VisualElement();
                row.AddToClassList("home-recent-row");

                var icon = new Label(record.Success ? "✓" : "✗");
                icon.AddToClassList("home-recent-icon");
                icon.AddToClassList(record.Success ? "home-recent-ok" : "home-recent-fail");
                row.Add(icon);

                var msg = new Label(record.Message);
                msg.AddToClassList("home-recent-msg");
                row.Add(msg);

                _recentList.Add(row);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 依赖感知 UI（沿用原有逻辑）
        // ═══════════════════════════════════════════════════════════

        private void RefreshDependencyUI(WorkspaceState state)
        {
            _bannerContainer?.Clear();

            var coreHealth     = state.CoreHealth.Value;
            var shaderHealth   = state.ShaderLibraryHealth.Value;
            var materialHealth = state.MaterialLibraryHealth.Value;
            var stateHealth    = state.StateRenderHealth.Value;

            if (coreHealth != PackageHealth.Installed)
            {
                _bannerContainer.Add(CreateBanner(
                    BannerLevel.Error,
                    "⚠ 需要安装 HMIRP 渲染管线",
                    "缺少 HMIRP Core 包，材质预览、场景搭建和批量替换功能不可用。"));
            }

            if (shaderHealth != PackageHealth.Installed && coreHealth == PackageHealth.Installed)
            {
                _bannerContainer.Add(CreateBanner(
                    BannerLevel.Warning,
                    "⚠ 材质预览和应用不可用",
                    "缺少 HMIRP Shader Library，材质无法正确渲染。"));
            }

            if (materialHealth != PackageHealth.Installed)
            {
                _bannerContainer.Add(CreateBanner(
                    BannerLevel.Warning,
                    "⚠ 材质库为空",
                    "缺少 HMIRP Material Library，需安装后才能浏览和应用材质。"));
            }

            if (stateHealth != PackageHealth.Installed)
            {
                _bannerContainer.Add(CreateBanner(
                    BannerLevel.Info,
                    "ℹ 高级状态效果不可用",
                    "缺少 State Render System，不影响主流程。"));
            }

            RefreshStatusAndRecommendation();
        }

        // ═══════════════════════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════════════════════

        private enum BannerLevel { Info, Warning, Error }

        private static VisualElement CreateBanner(BannerLevel level, string title, string message)
        {
            var banner = new VisualElement();
            banner.AddToClassList("home-banner");
            banner.AddToClassList(level switch
            {
                BannerLevel.Error   => "home-banner--error",
                BannerLevel.Warning => "home-banner--warning",
                _                   => "home-banner--info",
            });

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("home-banner-title");
            banner.Add(titleLabel);

            var msgLabel = new Label(message);
            msgLabel.AddToClassList("home-banner-message");
            banner.Add(msgLabel);

            return banner;
        }

        private static void SetCardEnabled(VisualElement card, bool enabled, string disabledReason)
        {
            if (card == null) return;

            var oldHint = card.Q<Label>(className: "home-card-disabled-hint");
            oldHint?.RemoveFromHierarchy();

            if (enabled)
            {
                card.RemoveFromClassList("home-card--disabled");
                card.SetEnabled(true);
            }
            else
            {
                card.AddToClassList("home-card--disabled");
                card.SetEnabled(false);

                if (!string.IsNullOrEmpty(disabledReason))
                {
                    var hintLabel = new Label(disabledReason);
                    hintLabel.AddToClassList("home-card-disabled-hint");
                    card.Add(hintLabel);
                }
            }
        }

        private static VisualElement CreateCompactCard(string icon, string iconClass,
            string title, string desc, Action onClick)
        {
            var card = new VisualElement();
            card.AddToClassList("home-card");
            card.AddToClassList("home-card-compact");

            card.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());

            var row = new VisualElement();
            row.AddToClassList("home-card-compact-row");

            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("home-card-icon");
            iconLabel.AddToClassList(iconClass);
            row.Add(iconLabel);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("home-card-title");
            titleLabel.AddToClassList("home-card-title-compact");
            textCol.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("home-card-desc");
            textCol.Add(descLabel);

            row.Add(textCol);

            var arrow = new Label("→");
            arrow.AddToClassList("home-card-arrow");
            row.Add(arrow);

            card.Add(row);
            return card;
        }
    }
}
