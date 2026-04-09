using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 场景构建工作区 — 增量重构 v3。
    ///
    /// 与 SceneController 协作：
    ///   - Controller 负责：模板管理、场景生成、State Render 感知、上下文发布
    ///   - View 负责：UI 构建、用户交互、视觉反馈、State Render 降级标识
    ///
    /// 三面板结构：
    ///   左：模板选择列表
    ///   中：视觉预览（渐变大气 + 构图线 + 场景信息叠加 + 配置摘要）
    ///   右：上下文配置（灯光/视角/天气/地面/天空选项卡片 + 生成按钮）
    /// </summary>
    public sealed class SceneBuilderView
    {
        private readonly VisualElement _root;
        private readonly WorkspaceState _workspaceState;
        private SceneController _controller;
        private SceneBuilderState _sb;

        // UI 缓存 — 左
        private VisualElement _templateList;

        // UI 缓存 — 中
        private VisualElement _previewRender;
        private VisualElement _previewGradient;
        private Label _previewSceneName;
        private Label _previewEnvHint;
        private Label _previewIcon;
        private Label _overlayTitle;
        private Label _overlayDesc;
        private Label _overlayEnvPill;
        private VisualElement _overlayFeatures;
        private VisualElement _configSummary;

        // UI 缓存 — 右
        private Label _rightHeader;
        private Label _rightSubheader;
        private Label _rightEnvPill;
        private Label _lightingSectionTitle;
        private Label _cameraSectionTitle;
        private Label _weatherSectionTitle;
        private Label _floorSectionTitle;
        private Label _skySectionTitle;
        private VisualElement _lightingGroup;
        private VisualElement _cameraGroup;
        private VisualElement _weatherGroup;
        private VisualElement _floorGroup;
        private VisualElement _skyGroup;
        private Button _generateBtn;
        private Label _statusLabel;

        // State Render 降级提示
        private VisualElement _stateRenderBanner;

        public SceneBuilderView(VisualElement root, WorkspaceState state)
        {
            _root = root;
            _workspaceState = state;
        }

        public void Bind(SceneController controller)
        {
            _controller = controller;
            _sb = controller.BuilderState;

            if (_root == null) return;
            _root.Clear();
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.flexGrow = 1;

            BuildLeftPanel();
            BuildCenterPanel();
            BuildRightPanel();

            // ── 响应式绑定 ──
            _sb.SelectedTemplate.Changed += (_, t) => RefreshAllPanels(t);
            _sb.GenerateStatus.Changed += (_, s) => { if (_statusLabel != null) _statusLabel.text = s; };

            // 每个配置变化 → 更新中面板摘要 + 右面板选中态
            // 上下文发布由 SceneController 负责，View 不再发布
            _sb.LightingPresetId.Changed += (_, _) => { RefreshConfigSummary(); RefreshPresetGroup(_lightingGroup, _sb.LightingPresetId.Value); };
            _sb.CameraPresetId.Changed   += (_, _) => { RefreshConfigSummary(); RefreshPresetGroup(_cameraGroup, _sb.CameraPresetId.Value); };
            _sb.WeatherId.Changed        += (_, _) => { RefreshConfigSummary(); RefreshPresetGroup(_weatherGroup, _sb.WeatherId.Value); };
            _sb.FloorId.Changed          += (_, _) => { RefreshConfigSummary(); RefreshPresetGroup(_floorGroup, _sb.FloorId.Value); };
            _sb.SkyId.Changed            += (_, _) => { RefreshConfigSummary(); RefreshPresetGroup(_skyGroup, _sb.SkyId.Value); };

            // State Render 变化 → 刷新降级标识
            _workspaceState.StateRenderHealth.Changed += (_, _) => RefreshDegradedBadges();

            // 注意：不在此处选择默认模板。
            // SceneController.Initialize() 在 BindViews 之后调用，
            // 会选择默认模板并触发级联更新。
        }

        // ════════════════════════════════════════════════════════════════
        // 左面板：模板卡片列表
        // ════════════════════════════════════════════════════════════════

        private void BuildLeftPanel()
        {
            var panel = MakePanel("ws-left-panel");
            panel.style.width = 250;
            panel.style.minWidth = 210;

            var header = new VisualElement();
            header.AddToClassList("ws-panel-header");
            header.Add(PanelTitle("\u573A\u666F\u6A21\u677F"));
            var badge = new Label($"{_sb.Templates.Value.Count}");
            badge.AddToClassList("ws-panel-badge");
            header.Add(badge);
            panel.Add(header);

            var hint = new Label("\u9009\u62E9\u573A\u666F\u6A21\u677F\u4EE5\u914D\u7F6E\u9884\u89C8");
            hint.AddToClassList("ws-hint");
            hint.style.marginBottom = 8;
            panel.Add(hint);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            _templateList = new VisualElement();
            foreach (var t in _sb.Templates.Value)
                _templateList.Add(BuildTemplateCard(t));
            scroll.Add(_templateList);
            panel.Add(scroll);
            _root.Add(panel);
        }

        private VisualElement BuildTemplateCard(SceneTemplate t)
        {
            var card = new VisualElement();
            card.AddToClassList("sb-tpl-card");
            card.userData = t;

            // 顶行：图标 + 名称 + 分类
            var top = new VisualElement();
            top.AddToClassList("sb-tpl-top");

            var icon = new Label(t.Icon ?? "\u25A1");
            icon.AddToClassList("sb-tpl-icon");
            top.Add(icon);

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;

            var nameLabel = new Label(t.Name);
            nameLabel.AddToClassList("sb-tpl-name");
            nameCol.Add(nameLabel);

            var catLabel = new Label(t.Category);
            catLabel.AddToClassList("sb-tpl-cat");
            nameCol.Add(catLabel);

            top.Add(nameCol);
            card.Add(top);

            // 效果描述
            var usage = new Label(t.UsageHint ?? t.Description);
            usage.AddToClassList("sb-tpl-usage");
            card.Add(usage);

            // 环境标签
            if (!string.IsNullOrEmpty(t.EnvironmentLabel))
            {
                var envPill = new Label(t.EnvironmentLabel);
                envPill.AddToClassList("sb-tpl-env");
                card.Add(envPill);
            }

            // 特性计数
            if (t.Features != null && t.Features.Length > 0)
            {
                var countLabel = new Label($"{t.Features.Length} \u9879\u7279\u6027");
                countLabel.AddToClassList("sb-tpl-count");
                card.Add(countLabel);
            }

            // 点击 → 委托 Controller 处理选择
            card.RegisterCallback<ClickEvent>(_ =>
            {
                _controller.SelectTemplate(t);
            });

            return card;
        }

        // ════════════════════════════════════════════════════════════════
        // 中面板：视觉预览（渲染感）
        // ════════════════════════════════════════════════════════════════

        private void BuildCenterPanel()
        {
            var panel = MakePanel("ws-center-panel");
            panel.style.flexGrow = 1;

            var area = new VisualElement();
            area.AddToClassList("sb-preview-area");

            // ── 渲染容器 ──
            _previewRender = new VisualElement();
            _previewRender.AddToClassList("sb-preview-render");

            // 大气渐变层（根据模板变色）
            _previewGradient = new VisualElement();
            _previewGradient.AddToClassList("sb-preview-gradient");
            _previewRender.Add(_previewGradient);

            // 构图参考线
            var grid = new VisualElement();
            grid.AddToClassList("sb-preview-grid");
            _previewRender.Add(grid);

            // 场景名（大字水印）
            _previewSceneName = new Label("Scene Builder");
            _previewSceneName.AddToClassList("sb-render-scene-name");
            _previewRender.Add(_previewSceneName);

            // 环境提示
            _previewEnvHint = new Label("");
            _previewEnvHint.AddToClassList("sb-render-env-hint");
            _previewRender.Add(_previewEnvHint);

            // 场景图标
            _previewIcon = new Label("\u25A1");
            _previewIcon.AddToClassList("sb-render-icon");
            _previewRender.Add(_previewIcon);

            // 渲染区角标："Preview"
            var cornerTag = new Label("PREVIEW");
            cornerTag.AddToClassList("sb-render-corner-tag");
            _previewRender.Add(cornerTag);

            area.Add(_previewRender);

            // ── 信息叠加层 ──
            var overlay = new VisualElement();
            overlay.AddToClassList("sb-preview-overlay");

            _overlayTitle = new Label("\u9009\u62E9\u6A21\u677F");
            _overlayTitle.AddToClassList("sb-preview-title");
            overlay.Add(_overlayTitle);

            _overlayDesc = new Label("\u4ECE\u5DE6\u4FA7\u9009\u62E9\u573A\u666F\u6A21\u677F\u5F00\u59CB\u914D\u7F6E");
            _overlayDesc.AddToClassList("sb-preview-subtitle");
            overlay.Add(_overlayDesc);

            _overlayEnvPill = new Label();
            _overlayEnvPill.AddToClassList("sb-preview-env-pill");
            _overlayEnvPill.style.display = DisplayStyle.None;
            overlay.Add(_overlayEnvPill);

            _overlayFeatures = new VisualElement();
            _overlayFeatures.AddToClassList("sb-feature-list");
            overlay.Add(_overlayFeatures);

            area.Add(overlay);

            // ── State Render 降级横幅 ──
            _stateRenderBanner = new VisualElement();
            _stateRenderBanner.AddToClassList("sb-sr-banner");
            _stateRenderBanner.style.display = _controller.IsStateRenderAvailable
                ? DisplayStyle.None : DisplayStyle.Flex;
            var srIcon = new Label("\u26A0");
            srIcon.AddToClassList("sb-sr-banner-icon");
            _stateRenderBanner.Add(srIcon);
            var srText = new Label("State Render System \u672A\u5B89\u88C5\uFF0C\u90E8\u5206\u9AD8\u7EA7\u6548\u679C\u5C06\u964D\u7EA7\u663E\u793A");
            srText.AddToClassList("sb-sr-banner-text");
            _stateRenderBanner.Add(srText);
            area.Add(_stateRenderBanner);

            // ── 配置摘要 ──
            _configSummary = new VisualElement();
            _configSummary.AddToClassList("sb-config-summary");
            _configSummary.style.display = DisplayStyle.None;
            area.Add(_configSummary);

            panel.Add(area);
            _root.Add(panel);
        }

        // ════════════════════════════════════════════════════════════════
        // 右面板：上下文配置 — 完全跟随选中场景
        // ════════════════════════════════════════════════════════════════

        private void BuildRightPanel()
        {
            var panel = MakePanel("ws-right-panel");
            panel.style.width = 290;
            panel.style.minWidth = 230;

            // ── 上下文标题区 ──
            var headerArea = new VisualElement();
            headerArea.AddToClassList("sb-ctx-header");

            _rightHeader = new Label("\u5F53\u524D\u573A\u666F");
            _rightHeader.AddToClassList("sb-ctx-title");
            headerArea.Add(_rightHeader);

            _rightSubheader = new Label("\u9009\u62E9\u6A21\u677F\u4EE5\u67E5\u770B\u914D\u7F6E\u9009\u9879");
            _rightSubheader.AddToClassList("sb-ctx-subtitle");
            headerArea.Add(_rightSubheader);

            _rightEnvPill = new Label();
            _rightEnvPill.AddToClassList("sb-ctx-env-pill");
            _rightEnvPill.style.display = DisplayStyle.None;
            headerArea.Add(_rightEnvPill);

            panel.Add(headerArea);

            // 分割线
            panel.Add(MakeSep());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            // ── 灯光 ──
            _lightingSectionTitle = SectionTitle("\u706F\u5149");
            scroll.Add(_lightingSectionTitle);
            _lightingGroup = new VisualElement();
            _lightingGroup.AddToClassList("sb-option-group");
            AddOptionCard(_lightingGroup, "studio",      "\u2606", "\u6444\u5F71\u68DA\u706F\u5149",  "\u67D4\u548C\u5747\u5300\uFF0C\u9002\u7528\u4E8E\u6750\u8D28\u8BC4\u5BA1",       _sb.LightingPresetId);
            AddOptionCard(_lightingGroup, "hdri-day",    "\u263C", "\u81EA\u7136\u65E5\u5149",    "HDRI \u65E5\u95F4\u73AF\u5883\uFF0C\u771F\u5B9E\u5149\u7167\u6548\u679C",    _sb.LightingPresetId);
            AddOptionCard(_lightingGroup, "hdri-sunset", "\u25D1", "\u65E5\u843D\u6696\u5149",    "\u91D1\u8272\u6696\u8C03\uFF0C\u9002\u7528\u4E8E\u6C1B\u56F4\u5C55\u793A",       _sb.LightingPresetId);
            AddOptionCard(_lightingGroup, "hdri-night",  "\u25CF", "\u591C\u95F4\u51B7\u5149",    "\u84DD\u8C03\u6708\u5149\uFF0C\u9002\u7528\u4E8E\u706F\u5149\u6F14\u793A",       _sb.LightingPresetId);
            AddOptionCard(_lightingGroup, "three-point", "\u2756", "\u4E09\u70B9\u5E03\u5149",    "\u4E13\u4E1A\u5F71\u68DA\u6807\u51C6\uFF0CKey + Fill + Rim", _sb.LightingPresetId);
            AddOptionCard(_lightingGroup, "ring-light",  "\u25CB", "\u73AF\u5F62\u706F",     "360\u00B0 \u5747\u5300\u7167\u660E\uFF0C\u65E0\u6B7B\u89D2",      _sb.LightingPresetId);
            scroll.Add(_lightingGroup);

            // ── 相机 ──
            _cameraSectionTitle = SectionTitle("\u89C6\u89D2");
            scroll.Add(_cameraSectionTitle);
            _cameraGroup = new VisualElement();
            _cameraGroup.AddToClassList("sb-option-group");
            AddOptionCard(_cameraGroup, "orbit-60",      "\u25A3", "\u73AF\u7ED5\u5E7F\u89D2",   "FOV 60\u00B0\uFF0C\u5168\u8F66\u6982\u89C8",        _sb.CameraPresetId);
            AddOptionCard(_cameraGroup, "orbit-35",      "\u25A3", "\u73AF\u7ED5\u957F\u7126",   "FOV 35\u00B0\uFF0C\u7EC6\u8282\u805A\u7126",        _sb.CameraPresetId);
            AddOptionCard(_cameraGroup, "front-hero",    "\u25B2", "\u6B63\u9762\u82F1\u96C4",   "\u4F4E\u89D2\u5EA6\u6B63\u9762\uFF0C\u5F3A\u8C03\u6C14\u52BF",           _sb.CameraPresetId);
            AddOptionCard(_cameraGroup, "three-quarter", "\u25C7", "3/4 \u7ECF\u5178",   "\u6700\u5E38\u7528\u7684\u4EA7\u54C1\u5C55\u793A\u89D2\u5EA6",           _sb.CameraPresetId);
            AddOptionCard(_cameraGroup, "top-down",      "\u25BD", "\u4FEF\u89C6",      "\u4ECE\u4E0A\u65B9\u4FEF\u77B0\u6574\u8F66",               _sb.CameraPresetId);
            AddOptionCard(_cameraGroup, "interior",      "\u25AB", "\u8F66\u5185\u89C6\u89D2",   "\u6A21\u62DF\u9A7E\u9A76\u5458\u89C6\u89D2",               _sb.CameraPresetId);
            scroll.Add(_cameraGroup);

            // ── 天气（含 State Render 依赖标记）──
            _weatherSectionTitle = SectionTitle("\u5929\u6C14\u6C1B\u56F4");
            scroll.Add(_weatherSectionTitle);
            _weatherGroup = new VisualElement();
            _weatherGroup.AddToClassList("sb-option-group");
            _weatherGroup.AddToClassList("sb-option-group-compact");
            AddOptionCard(_weatherGroup, "sunny",  "\u2600", "\u6674\u5929", "\u660E\u4EAE\u6E05\u6670",   _sb.WeatherId);
            AddOptionCard(_weatherGroup, "cloudy", "\u2601", "\u9634\u5929", "\u67D4\u548C\u6563\u5C04",   _sb.WeatherId);
            AddOptionCard(_weatherGroup, "rainy",  "\u2602", "\u96E8\u5929", "\u6E7F\u6DA6\u53CD\u5C04",   _sb.WeatherId, requiresStateRender: true);
            AddOptionCard(_weatherGroup, "snowy",  "\u2746", "\u96EA\u5929", "\u51B7\u767D\u6C1B\u56F4",   _sb.WeatherId, requiresStateRender: true);
            AddOptionCard(_weatherGroup, "foggy",  "\u2592", "\u96FE\u5929", "\u6726\u80E7\u5C42\u6B21",   _sb.WeatherId, requiresStateRender: true);
            AddOptionCard(_weatherGroup, "sunset", "\u25D1", "\u9EC4\u660F", "\u6696\u91D1\u8272\u8C03",   _sb.WeatherId);
            scroll.Add(_weatherGroup);

            // ── 地面 ──
            _floorSectionTitle = SectionTitle("\u5730\u9762\u6750\u8D28");
            scroll.Add(_floorSectionTitle);
            _floorGroup = new VisualElement();
            _floorGroup.AddToClassList("sb-option-group");
            _floorGroup.AddToClassList("sb-option-group-compact");
            AddOptionCard(_floorGroup, "dark",        "\u25A0", "\u6DF1\u8272\u9AD8\u5149",  "\u9AD8\u53CD\u5C04\u955C\u9762\u6548\u679C",  _sb.FloorId);
            AddOptionCard(_floorGroup, "light",       "\u25A1", "\u6D45\u8272\u54D1\u5149",  "\u67D4\u548C\u6F2B\u53CD\u5C04",     _sb.FloorId);
            AddOptionCard(_floorGroup, "asphalt",     "\u2593", "\u67CF\u6CB9\u8DEF\u9762",  "\u771F\u5B9E\u9053\u8DEF\u8D28\u611F",    _sb.FloorId);
            AddOptionCard(_floorGroup, "concrete",    "\u2591", "\u6DF7\u51DD\u571F",   "\u7C97\u7CD9\u5DE5\u4E1A\u611F",      _sb.FloorId);
            AddOptionCard(_floorGroup, "grass",       "\u2261", "\u8349\u5730",     "\u81EA\u7136\u7EFF\u8272\u5730\u9762",     _sb.FloorId);
            AddOptionCard(_floorGroup, "transparent", "\u25CC", "\u900F\u660E",     "\u65E0\u5730\u9762\uFF0C\u60AC\u6D6E\u5C55\u793A",  _sb.FloorId);
            scroll.Add(_floorGroup);

            // ── 天空（含 State Render 依赖标记）──
            _skySectionTitle = SectionTitle("\u5929\u7A7A\u80CC\u666F");
            scroll.Add(_skySectionTitle);
            _skyGroup = new VisualElement();
            _skyGroup.AddToClassList("sb-option-group");
            _skyGroup.AddToClassList("sb-option-group-compact");
            AddOptionCard(_skyGroup, "gradient",   "\u25E4", "\u6E10\u53D8\u5929\u7A7A",    "\u5E73\u6ED1\u8272\u8C03\u8FC7\u6E21",     _sb.SkyId);
            AddOptionCard(_skyGroup, "solid",      "\u25A0", "\u7EAF\u8272\u80CC\u666F",    "\u6781\u7B80\u5E72\u51C0",        _sb.SkyId);
            AddOptionCard(_skyGroup, "hdri",       "\u25CB", "HDRI \u5929\u7A7A\u7403", "360\u00B0 \u73AF\u5883\u6620\u5C04", _sb.SkyId);
            AddOptionCard(_skyGroup, "procedural", "\u2601", "\u7A0B\u5E8F\u5316\u4E91\u5C42",   "\u52A8\u6001\u5929\u6C14\u4E91\u5C42",     _sb.SkyId, requiresStateRender: true);
            scroll.Add(_skyGroup);

            // ── 生成 ──
            scroll.Add(MakeSep());

            _generateBtn = new Button(() => _controller.GenerateScene()) { text = "\u25B6  \u751F\u6210\u573A\u666F" };
            _generateBtn.AddToClassList("sb-generate-btn");
            scroll.Add(_generateBtn);

            _statusLabel = new Label("");
            _statusLabel.AddToClassList("ws-hint");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4;
            scroll.Add(_statusLabel);

            panel.Add(scroll);
            _root.Add(panel);
        }

        /// <summary>创建一个选项卡片并加入 group。</summary>
        private void AddOptionCard(VisualElement group, string id, string icon, string name,
            string effect, Observable<string> target, bool requiresStateRender = false)
        {
            var card = new VisualElement();
            card.AddToClassList("sb-opt-card");
            card.userData = id;

            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("sb-opt-icon");
            card.Add(iconLabel);

            var textCol = new VisualElement();
            textCol.AddToClassList("sb-opt-text");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("sb-opt-name");
            textCol.Add(nameLabel);

            var effectLabel = new Label(effect);
            effectLabel.AddToClassList("sb-opt-effect");
            textCol.Add(effectLabel);

            card.Add(textCol);

            // 选中指示器
            var check = new Label("\u2713");
            check.AddToClassList("sb-opt-check");
            card.Add(check);

            // State Render 降级标识
            if (requiresStateRender)
            {
                card.AddToClassList("sb-opt-sr-dependent");
                var badge = new Label("\u26A0 \u9700\u8981 State Render");
                badge.AddToClassList("sb-opt-degraded-badge");
                badge.style.display = _controller.IsStateRenderAvailable
                    ? DisplayStyle.None : DisplayStyle.Flex;
                card.Add(badge);
            }

            if (target.Value == id) card.AddToClassList("active");

            card.RegisterCallback<ClickEvent>(_ =>
            {
                target.Value = id;
                foreach (var sibling in group.Children())
                {
                    if (sibling.ClassListContains("sb-opt-card"))
                        sibling.RemoveFromClassList("active");
                }
                card.AddToClassList("active");
            });

            group.Add(card);
        }

        // ════════════════════════════════════════════════════════════════
        // State Render 降级刷新
        // ════════════════════════════════════════════════════════════════

        private void RefreshDegradedBadges()
        {
            bool available = _controller.IsStateRenderAvailable;

            // 刷新所有标记了 sr-dependent 的选项卡片
            RefreshGroupDegradedBadges(_weatherGroup, available);
            RefreshGroupDegradedBadges(_skyGroup, available);

            // 刷新中面板横幅
            if (_stateRenderBanner != null)
                _stateRenderBanner.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void RefreshGroupDegradedBadges(VisualElement group, bool stateRenderAvailable)
        {
            if (group == null) return;
            foreach (var child in group.Children())
            {
                if (!child.ClassListContains("sb-opt-sr-dependent")) continue;
                var badge = child.Q<Label>(className: "sb-opt-degraded-badge");
                if (badge != null)
                    badge.style.display = stateRenderAvailable ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 联动刷新 — 选择模板后级联更新三个面板
        // ════════════════════════════════════════════════════════════════

        private void RefreshAllPanels(SceneTemplate t)
        {
            RefreshLeftPanel(t);
            RefreshCenterPreview(t);
            RefreshRightPanel(t);
        }

        // ── 左面板 ──
        private void RefreshLeftPanel(SceneTemplate t)
        {
            if (_templateList == null) return;
            foreach (var child in _templateList.Children())
            {
                if (child.userData == t)
                    child.AddToClassList("selected");
                else
                    child.RemoveFromClassList("selected");
            }
        }

        // ── 中面板 ──
        private void RefreshCenterPreview(SceneTemplate t)
        {
            // 渲染区水印
            if (_previewSceneName != null)
                _previewSceneName.text = t?.Name ?? "Scene Builder";
            if (_previewEnvHint != null)
                _previewEnvHint.text = t?.EnvironmentLabel ?? "";
            if (_previewIcon != null)
                _previewIcon.text = t?.Icon ?? "\u25A1";

            // 渐变色氛围 — 根据模板 ID 切换预设色彩类
            if (_previewGradient != null)
            {
                _previewGradient.RemoveFromClassList("atmosphere-warm");
                _previewGradient.RemoveFromClassList("atmosphere-cool");
                _previewGradient.RemoveFromClassList("atmosphere-night");
                _previewGradient.RemoveFromClassList("atmosphere-neutral");
                _previewGradient.RemoveFromClassList("atmosphere-golden");
                _previewGradient.RemoveFromClassList("atmosphere-dim");

                if (t != null)
                {
                    var atmo = t.Id switch
                    {
                        "showroom"     => "atmosphere-warm",
                        "outdoor-road" => "atmosphere-cool",
                        "studio"       => "atmosphere-neutral",
                        "night-city"   => "atmosphere-night",
                        "turntable"    => "atmosphere-neutral",
                        "parking"      => "atmosphere-dim",
                        _              => "atmosphere-neutral",
                    };
                    _previewGradient.AddToClassList(atmo);
                }
            }

            // 叠加层
            if (_overlayTitle != null)
                _overlayTitle.text = t?.Name ?? "\u9009\u62E9\u6A21\u677F";
            if (_overlayDesc != null)
                _overlayDesc.text = t?.Description ?? "\u4ECE\u5DE6\u4FA7\u9009\u62E9\u573A\u666F\u6A21\u677F\u5F00\u59CB\u914D\u7F6E";

            if (_overlayEnvPill != null)
            {
                if (t?.EnvironmentLabel != null)
                {
                    _overlayEnvPill.text = t.EnvironmentLabel;
                    _overlayEnvPill.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _overlayEnvPill.style.display = DisplayStyle.None;
                }
            }

            // 特性标签
            if (_overlayFeatures != null)
            {
                _overlayFeatures.Clear();
                if (t?.Features != null)
                {
                    foreach (var f in t.Features)
                    {
                        var chip = new Label(f);
                        chip.AddToClassList("sb-feature-chip");
                        _overlayFeatures.Add(chip);
                    }
                }
            }

            RefreshConfigSummary();
        }

        // ── 右面板 ──
        private void RefreshRightPanel(SceneTemplate t)
        {
            // 上下文标题
            if (_rightHeader != null)
                _rightHeader.text = t != null ? $"\u5F53\u524D\u573A\u666F\uFF1A{t.Name}" : "\u5F53\u524D\u573A\u666F";

            if (_rightSubheader != null)
                _rightSubheader.text = t?.UsageHint ?? "\u9009\u62E9\u6A21\u677F\u4EE5\u67E5\u770B\u914D\u7F6E\u9009\u9879";

            if (_rightEnvPill != null)
            {
                if (t?.EnvironmentLabel != null)
                {
                    _rightEnvPill.text = t.EnvironmentLabel;
                    _rightEnvPill.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _rightEnvPill.style.display = DisplayStyle.None;
                }
            }

            // 上下文分组标题
            var sceneName = t?.Name ?? "";
            if (_lightingSectionTitle != null)
                _lightingSectionTitle.text = sceneName.Length > 0 ? $"\u706F\u5149\uFF08{sceneName}\uFF09" : "\u706F\u5149";
            if (_cameraSectionTitle != null)
                _cameraSectionTitle.text = sceneName.Length > 0 ? $"\u89C6\u89D2\uFF08{sceneName}\uFF09" : "\u89C6\u89D2";
            if (_weatherSectionTitle != null)
                _weatherSectionTitle.text = sceneName.Length > 0 ? $"\u5929\u6C14\u6C1B\u56F4\uFF08{sceneName}\uFF09" : "\u5929\u6C14\u6C1B\u56F4";
            if (_floorSectionTitle != null)
                _floorSectionTitle.text = sceneName.Length > 0 ? $"\u5730\u9762\u6750\u8D28\uFF08{sceneName}\uFF09" : "\u5730\u9762\u6750\u8D28";
            if (_skySectionTitle != null)
                _skySectionTitle.text = sceneName.Length > 0 ? $"\u5929\u7A7A\u80CC\u666F\uFF08{sceneName}\uFF09" : "\u5929\u7A7A\u80CC\u666F";

            // 刷新选中态
            RefreshPresetGroup(_lightingGroup, _sb.LightingPresetId.Value);
            RefreshPresetGroup(_cameraGroup,   _sb.CameraPresetId.Value);
            RefreshPresetGroup(_weatherGroup,  _sb.WeatherId.Value);
            RefreshPresetGroup(_floorGroup,    _sb.FloorId.Value);
            RefreshPresetGroup(_skyGroup,      _sb.SkyId.Value);
        }

        private static void RefreshPresetGroup(VisualElement group, string activeId)
        {
            if (group == null) return;
            foreach (var child in group.Children())
            {
                if (!child.ClassListContains("sb-opt-card")) continue;
                if (child.userData is string btnId && btnId == activeId)
                    child.AddToClassList("active");
                else
                    child.RemoveFromClassList("active");
            }
        }

        private void RefreshConfigSummary()
        {
            if (_configSummary == null) return;
            _configSummary.Clear();

            var t = _sb.SelectedTemplate.Value;
            if (t == null) { _configSummary.style.display = DisplayStyle.None; return; }

            _configSummary.style.display = DisplayStyle.Flex;

            var title = new Label($"{t.Name} \u2022 \u5F53\u524D\u914D\u7F6E");
            title.AddToClassList("sb-summary-title");
            _configSummary.Add(title);

            AddSummaryRow("\u706F\u5149", SceneController.GetLightingName(_sb.LightingPresetId.Value));
            AddSummaryRow("\u89C6\u89D2", SceneController.GetCameraName(_sb.CameraPresetId.Value));

            var weatherName = SceneController.GetWeatherName(_sb.WeatherId.Value);
            if (_controller.IsAdvancedWeather(_sb.WeatherId.Value) && !_controller.IsStateRenderAvailable)
                weatherName += " \u26A0";
            AddSummaryRow("\u5929\u6C14", weatherName);

            AddSummaryRow("\u5730\u9762", SceneController.GetFloorName(_sb.FloorId.Value));

            var skyName = SceneController.GetSkyName(_sb.SkyId.Value);
            if (_controller.IsAdvancedSky(_sb.SkyId.Value) && !_controller.IsStateRenderAvailable)
                skyName += " \u26A0";
            AddSummaryRow("\u5929\u7A7A", skyName);
        }

        private void AddSummaryRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ws-form-row");
            row.Add(new Label(label) { name = "lbl" }.WithClass("ws-form-label"));
            row.Add(new Label(value) { name = "val" }.WithClass("ws-form-value"));
            _configSummary.Add(row);
        }

        // ════════════════════════════════════════════════════════════════
        // 辅助
        // ════════════════════════════════════════════════════════════════

        private static VisualElement MakePanel(string cls)
        {
            var p = new VisualElement();
            p.AddToClassList("ws-panel");
            p.AddToClassList(cls);
            return p;
        }

        private static Label PanelTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ws-panel-title");
            return l;
        }

        private static Label SectionTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("sb-section-title");
            return l;
        }

        private static VisualElement MakeSep()
        {
            var s = new VisualElement();
            s.AddToClassList("sb-separator");
            return s;
        }
    }

    // Extension needed by AddSummaryRow inline usage
    internal static class LabelExt
    {
        public static Label WithClass(this Label label, string className)
        {
            label.AddToClassList(className);
            return label;
        }
    }
}
