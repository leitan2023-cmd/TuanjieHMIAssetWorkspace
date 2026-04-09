using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 右面板 — 统一上下文面板。
    ///
    /// 根据当前 ViewMode 自动切换显示不同上下文：
    ///   Asset Browser → 3 步操作面板（选择目标→选择资产→应用）
    ///   BatchReplace  → 替换上下文面板（目标、当前材质→替换材质、影响范围、状态）
    ///   其他模式      → 跨模式信号驱动的通用信息面板
    /// </summary>
    public sealed class InspectorPanelView : IInspectorView
    {
        private readonly VisualElement _root;
        private ActionController _actionController;
        private WorkspaceState _state;

        // ── 上下文容器 ──
        private VisualElement _assetBrowserPanel;
        private VisualElement _batchReplacePanel;
        private VisualElement _genericPanel;

        // 当前活跃的上下文模式
        private string _activeContext = "";

        // ══ Asset Browser 面板元素 ══
        private Label _step1Indicator;
        private VisualElement _step1Content;
        private Label _step1Empty;
        private Label _targetName;
        private Label _targetRenderer;
        private Label _targetMatName;
        private Image _targetMatPreview;

        private Label _step2Indicator;
        private VisualElement _step2Content;
        private Label _step2Empty;
        private Image _assetThumb;
        private Label _assetName;
        private Label _assetBadge;
        private Label _assetPath;

        private Label _step3Indicator;
        private VisualElement _step3Content;
        private Label _step3Empty;
        private Button _applyBtn;
        private Label _reasonLabel;

        // ══ BatchReplace 面板元素 ══
        private Label _brTargetLabel;
        private Label _brCurrentMatLabel;
        private Image _brCurrentMatThumb;
        private Label _brNewMatLabel;
        private Image _brNewMatThumb;
        private Label _brArrow;
        private Label _brImpactLabel;
        private Label _brStatusLabel;
        private Label _brLastResultLabel;

        // ══ SceneBuilder 面板元素 ══
        private VisualElement _sceneBuilderPanel;
        private Label _sbTemplateName;
        private Label _sbTemplateSubtitle;
        private Label _sbConfigDetail;
        private Label _sbStateRenderLabel;
        private Label _sbHint;

        // ══ VehicleSetup 面板元素 ══
        private VisualElement _vehicleSetupPanel;
        private Label _vsPartName;
        private Label _vsPartSubtitle;
        private Label _vsStatusLabel;
        private Label _vsDetailLabel;
        private Label _vsHint;

        // ══ Generic 面板元素 ══
        private Label _genTitle;
        private Label _genSubtitle;
        private Label _genDetail;
        private Label _genHint;
        private Image _genPreview;

        // 异步材质预览
        private Material _pendingMaterial;

        public InspectorPanelView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, ActionController actionController)
        {
            if (_root == null) return;
            _actionController = actionController;
            _state = state;

            _root.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _root.Add(scroll);

            // ── 构建三套面板（同时存在，按上下文切换可见性）──
            BuildAssetBrowserPanel(scroll);
            BuildBatchReplacePanel(scroll);
            BuildSceneBuilderPanel(scroll);
            BuildVehicleSetupPanel(scroll);
            BuildGenericPanel(scroll);

            // ── 响应式绑定 ──

            // ViewMode 变化 → 切换上下文面板
            state.CurrentViewMode.Changed += (_, mode) => SwitchContext(mode);

            // 资产浏览器上下文
            state.UnitySelection.Changed += (_, obj) =>
            {
                RefreshStep1(obj);
                RefreshBRTarget();
            };
            state.SelectedAsset.Changed += (_, asset) => RefreshStep2Asset(asset);

            // 跨模式信号
            SelectionEvents.ContextChanged.Subscribe(OnContextChanged);
            SelectionEvents.ContextCleared.Subscribe(OnContextCleared);

            // 操作按钮状态（含 Undo/Redo 触发 — 按当前活跃面板局部刷新）
            ActionEvents.StatesChanged.Subscribe(_ =>
            {
                using var _t = PerfTrace.Begin("InspectorPanel.OnStatesChanged");
                switch (_activeContext)
                {
                    case "AssetBrowser":
                        RefreshStep1(_state.UnitySelection.Value);
                        RefreshStep3();
                        break;
                    case "BatchReplace":
                        RefreshBRTarget();
                        RefreshBRStatus();
                        break;
                    default:
                        // Compare / SceneBuilder / VehicleSetup / Generic — 仅刷新通用部分
                        break;
                }
            });

            // 操作结果 → 更新 BatchReplace 面板
            ActionEvents.Executed.Subscribe(evt =>
            {
                if (_brLastResultLabel != null && _activeContext == "BatchReplace")
                    _brLastResultLabel.text = $"\u2713 {evt.Message}";
            });
            ActionEvents.Failed.Subscribe(evt =>
            {
                if (_brLastResultLabel != null && _activeContext == "BatchReplace")
                    _brLastResultLabel.text = $"\u2717 {evt.Reason}";
            });

            // 预览纹理就绪
            PreviewEvents.TextureReady.Subscribe(evt =>
            {
                if (_assetThumb != null && evt.Texture != null)
                    _assetThumb.image = evt.Texture;
            });

            // State Render 状态 → SceneBuilder 面板
            state.StateRenderHealth.Changed += (_, _) => RefreshSBStateRender();

            // 初始状态
            SwitchContext(state.CurrentViewMode.Value);
            RefreshStep1(state.UnitySelection.Value);
            RefreshStep2Asset(state.SelectedAsset.Value);
            RefreshStep3();
        }

        // ════════════════════════════════════════════════════════════════
        // 上下文切换
        // ════════════════════════════════════════════════════════════════

        private void SwitchContext(ViewMode mode)
        {
            switch (mode)
            {
                case ViewMode.Grid:
                    ShowPanel("AssetBrowser");
                    break;
                case ViewMode.BatchReplace:
                    ShowPanel("BatchReplace");
                    RefreshBRTarget();
                    RefreshBRStatus();
                    break;
                case ViewMode.SceneBuilder:
                    ShowPanel("SceneBuilder");
                    RefreshSBStateRender();
                    break;
                case ViewMode.VehicleSetup:
                    ShowPanel("VehicleSetup");
                    break;
                default:
                    ShowPanel("Generic");
                    break;
            }
        }

        private void ShowPanel(string context)
        {
            _activeContext = context;
            SetVisible(_assetBrowserPanel, context == "AssetBrowser");
            SetVisible(_batchReplacePanel, context == "BatchReplace");
            SetVisible(_sceneBuilderPanel, context == "SceneBuilder");
            SetVisible(_vehicleSetupPanel, context == "VehicleSetup");
            SetVisible(_genericPanel, context == "Generic");
        }

        // ════════════════════════════════════════════════════════════════
        // 面板 A：Asset Browser — 3 步操作
        // ════════════════════════════════════════════════════════════════

        private void BuildAssetBrowserPanel(VisualElement parent)
        {
            _assetBrowserPanel = new VisualElement();
            _assetBrowserPanel.AddToClassList("ctx-panel");

            var header = new Label("\u8D44\u4EA7\u64CD\u4F5C");
            header.AddToClassList("ctx-panel-header");
            _assetBrowserPanel.Add(header);

            BuildStep1(_assetBrowserPanel);
            BuildStep2(_assetBrowserPanel);
            BuildStep3(_assetBrowserPanel);

            parent.Add(_assetBrowserPanel);
        }

        private void BuildStep1(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("act-step-row");

            _step1Indicator = new Label("1");
            _step1Indicator.AddToClassList("act-step-indicator");
            _step1Indicator.AddToClassList("act-step-pending");
            row.Add(_step1Indicator);

            var body = new VisualElement();
            body.AddToClassList("act-step-body");

            var title = new Label("Step 1: \u9009\u62E9\u76EE\u6807");
            title.AddToClassList("act-step-title");
            body.Add(title);

            _step1Content = new VisualElement();
            _step1Content.AddToClassList("act-step-content");
            _step1Content.style.display = DisplayStyle.None;

            _targetName = new Label("");
            _targetName.AddToClassList("act-target-name");
            _step1Content.Add(_targetName);

            _targetRenderer = new Label("");
            _targetRenderer.AddToClassList("act-target-renderer");
            _step1Content.Add(_targetRenderer);

            var matRow = new VisualElement();
            matRow.AddToClassList("act-target-mat-row");

            _targetMatPreview = new Image();
            _targetMatPreview.AddToClassList("act-target-preview");
            _targetMatPreview.style.display = DisplayStyle.None;
            matRow.Add(_targetMatPreview);

            _targetMatName = new Label("");
            _targetMatName.AddToClassList("act-target-mat-name");
            matRow.Add(_targetMatName);

            _step1Content.Add(matRow);
            body.Add(_step1Content);

            _step1Empty = new Label("\u5728 Hierarchy \u4E2D\u9009\u62E9\u4E00\u4E2A GameObject");
            _step1Empty.AddToClassList("act-step-empty");
            body.Add(_step1Empty);

            row.Add(body);
            parent.Add(row);
        }

        private void BuildStep2(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("act-step-row");

            _step2Indicator = new Label("2");
            _step2Indicator.AddToClassList("act-step-indicator");
            _step2Indicator.AddToClassList("act-step-pending");
            row.Add(_step2Indicator);

            var body = new VisualElement();
            body.AddToClassList("act-step-body");

            var title = new Label("Step 2: \u9009\u62E9\u8D44\u4EA7");
            title.AddToClassList("act-step-title");
            body.Add(title);

            _step2Content = new VisualElement();
            _step2Content.AddToClassList("act-step-content");
            _step2Content.style.display = DisplayStyle.None;

            var assetRow = new VisualElement();
            assetRow.AddToClassList("act-asset-row");

            _assetThumb = new Image();
            _assetThumb.AddToClassList("act-asset-thumb");
            _assetThumb.scaleMode = ScaleMode.ScaleAndCrop;
            assetRow.Add(_assetThumb);

            var infoCol = new VisualElement();
            infoCol.AddToClassList("act-asset-info");

            _assetName = new Label("");
            _assetName.AddToClassList("act-asset-name");
            infoCol.Add(_assetName);

            _assetBadge = new Label("");
            _assetBadge.AddToClassList("act-asset-badge");
            infoCol.Add(_assetBadge);

            _assetPath = new Label("");
            _assetPath.AddToClassList("act-asset-path");
            infoCol.Add(_assetPath);

            assetRow.Add(infoCol);
            _step2Content.Add(assetRow);
            body.Add(_step2Content);

            _step2Empty = new Label("\u4ECE\u4E2D\u95F4\u5217\u8868\u70B9\u51FB\u4E00\u4E2A\u8D44\u4EA7");
            _step2Empty.AddToClassList("act-step-empty");
            body.Add(_step2Empty);

            row.Add(body);
            parent.Add(row);
        }

        private void BuildStep3(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("act-step-row");
            row.style.borderBottomWidth = 0;

            _step3Indicator = new Label("3");
            _step3Indicator.AddToClassList("act-step-indicator");
            _step3Indicator.AddToClassList("act-step-pending");
            row.Add(_step3Indicator);

            var body = new VisualElement();
            body.AddToClassList("act-step-body");

            var title = new Label("Step 3: \u6267\u884C\u64CD\u4F5C");
            title.AddToClassList("act-step-title");
            body.Add(title);

            _step3Content = new VisualElement();
            _step3Content.AddToClassList("act-step-content");
            _step3Content.style.display = DisplayStyle.None;

            _applyBtn = new Button(() => _actionController?.ApplyToSelection())
            {
                text = "\u25B6  \u5E94\u7528\u5230\u573A\u666F"
            };
            _applyBtn.AddToClassList("act-apply-btn");
            _step3Content.Add(_applyBtn);

            _reasonLabel = new Label("");
            _reasonLabel.AddToClassList("act-reason");
            _step3Content.Add(_reasonLabel);

            var secondaryRow = new VisualElement();
            secondaryRow.AddToClassList("act-secondary-row");

            var undoBtn = new Button(() => _actionController?.PerformUndo())
            {
                text = "\u21B6 \u64A4\u9500"
            };
            undoBtn.AddToClassList("act-secondary-btn");
            secondaryRow.Add(undoBtn);

            _step3Content.Add(secondaryRow);
            body.Add(_step3Content);

            _step3Empty = new Label("\u5B8C\u6210 Step 1 \u548C Step 2 \u540E\u53EF\u6267\u884C\u64CD\u4F5C");
            _step3Empty.AddToClassList("act-step-empty");
            body.Add(_step3Empty);

            row.Add(body);
            parent.Add(row);
        }

        // ════════════════════════════════════════════════════════════════
        // 面板 B：BatchReplace — 替换上下文
        // ════════════════════════════════════════════════════════════════

        private void BuildBatchReplacePanel(VisualElement parent)
        {
            _batchReplacePanel = new VisualElement();
            _batchReplacePanel.AddToClassList("ctx-panel");
            _batchReplacePanel.style.display = DisplayStyle.None;

            var header = new Label("\u66FF\u6362\u4E0A\u4E0B\u6587");
            header.AddToClassList("ctx-panel-header");
            _batchReplacePanel.Add(header);

            // ── 目标对象 ──
            var targetSection = BuildInfoSection("\u76EE\u6807\u5BF9\u8C61");
            _brTargetLabel = new Label("\u672A\u9009\u4E2D");
            _brTargetLabel.AddToClassList("ctx-info-value");
            targetSection.Add(_brTargetLabel);
            _batchReplacePanel.Add(targetSection);

            // ── 材质对比 ──
            var matSection = new VisualElement();
            matSection.AddToClassList("ctx-section");

            var matTitle = new Label("\u6750\u8D28\u5BF9\u6BD4");
            matTitle.AddToClassList("ctx-section-title");
            matSection.Add(matTitle);

            var matCompare = new VisualElement();
            matCompare.AddToClassList("ctx-mat-compare");

            // 当前材质
            var currentCol = new VisualElement();
            currentCol.AddToClassList("ctx-mat-col");
            _brCurrentMatThumb = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _brCurrentMatThumb.AddToClassList("ctx-mat-thumb");
            currentCol.Add(_brCurrentMatThumb);
            _brCurrentMatLabel = new Label("\u5F53\u524D");
            _brCurrentMatLabel.AddToClassList("ctx-mat-name");
            currentCol.Add(_brCurrentMatLabel);
            matCompare.Add(currentCol);

            // 箭头
            _brArrow = new Label("\u2192");
            _brArrow.AddToClassList("ctx-mat-arrow");
            matCompare.Add(_brArrow);

            // 新材质
            var newCol = new VisualElement();
            newCol.AddToClassList("ctx-mat-col");
            _brNewMatThumb = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _brNewMatThumb.AddToClassList("ctx-mat-thumb");
            newCol.Add(_brNewMatThumb);
            _brNewMatLabel = new Label("\u672A\u9009\u62E9");
            _brNewMatLabel.AddToClassList("ctx-mat-name");
            newCol.Add(_brNewMatLabel);
            matCompare.Add(newCol);

            matSection.Add(matCompare);
            _batchReplacePanel.Add(matSection);

            // ── 影响范围 ──
            var impactSection = BuildInfoSection("\u5F71\u54CD\u8303\u56F4");
            _brImpactLabel = new Label("0 \u4E2A\u5BF9\u8C61");
            _brImpactLabel.AddToClassList("ctx-info-value");
            impactSection.Add(_brImpactLabel);
            _batchReplacePanel.Add(impactSection);

            // ── 操作状态 ──
            var statusSection = BuildInfoSection("\u64CD\u4F5C\u72B6\u6001");
            _brStatusLabel = new Label("\u7B49\u5F85\u9009\u62E9\u76EE\u6807\u548C\u5019\u9009\u6750\u8D28");
            _brStatusLabel.AddToClassList("ctx-info-value");
            _brStatusLabel.AddToClassList("ctx-status");
            statusSection.Add(_brStatusLabel);
            _batchReplacePanel.Add(statusSection);

            // ── 最近操作结果 ──
            var resultSection = BuildInfoSection("\u6700\u8FD1\u64CD\u4F5C");
            _brLastResultLabel = new Label("\u6682\u65E0");
            _brLastResultLabel.AddToClassList("ctx-info-value");
            _brLastResultLabel.AddToClassList("ctx-last-result");
            resultSection.Add(_brLastResultLabel);
            _batchReplacePanel.Add(resultSection);

            // ── 依赖提示 ──
            if (_state != null)
            {
                var depSection = BuildInfoSection("\u73AF\u5883\u72B6\u6001");
                var depLabel = new Label();
                depLabel.AddToClassList("ctx-info-value");
                depSection.Add(depLabel);
                _batchReplacePanel.Add(depSection);

                // 实时刷新依赖提示
                void RefreshDepLabel()
                {
                    var coreOk = _state.CoreHealth.Value == PackageHealth.Installed;
                    var shaderOk = _state.ShaderLibraryHealth.Value == PackageHealth.Installed;
                    if (coreOk && shaderOk)
                    {
                        depLabel.text = "\u2713 HMIRP Core + Shader Library \u5DF2\u5C31\u7EEA";
                        depLabel.RemoveFromClassList("ctx-dep-warning");
                        depLabel.AddToClassList("ctx-dep-ok");
                    }
                    else
                    {
                        var missing = !coreOk ? "HMIRP Core" : "Shader Library";
                        depLabel.text = $"\u26A0 {missing} \u672A\u5B89\u88C5\uFF0C\u66FF\u6362\u53EF\u80FD\u4E0D\u53EF\u7528";
                        depLabel.RemoveFromClassList("ctx-dep-ok");
                        depLabel.AddToClassList("ctx-dep-warning");
                    }
                }

                RefreshDepLabel();
                _state.CoreHealth.Changed += (_, _) => RefreshDepLabel();
                _state.ShaderLibraryHealth.Changed += (_, _) => RefreshDepLabel();
            }

            parent.Add(_batchReplacePanel);
        }

        // ════════════════════════════════════════════════════════════════
        // 面板 C：SceneBuilder — 场景构建上下文
        // ════════════════════════════════════════════════════════════════

        private void BuildSceneBuilderPanel(VisualElement parent)
        {
            _sceneBuilderPanel = new VisualElement();
            _sceneBuilderPanel.AddToClassList("ctx-panel");
            _sceneBuilderPanel.style.display = DisplayStyle.None;

            var header = new Label("\u573A\u666F\u6784\u5EFA");
            header.AddToClassList("ctx-panel-header");
            _sceneBuilderPanel.Add(header);

            // ── 当前模板 ──
            var tplSection = BuildInfoSection("\u5F53\u524D\u6A21\u677F");
            _sbTemplateName = new Label("\u672A\u9009\u62E9\u6A21\u677F");
            _sbTemplateName.AddToClassList("ctx-sb-template-name");
            tplSection.Add(_sbTemplateName);

            _sbTemplateSubtitle = new Label("");
            _sbTemplateSubtitle.AddToClassList("ctx-info-value");
            tplSection.Add(_sbTemplateSubtitle);
            _sceneBuilderPanel.Add(tplSection);

            // ── 配置摘要 ──
            var cfgSection = BuildInfoSection("\u914D\u7F6E\u6458\u8981");
            _sbConfigDetail = new Label("");
            _sbConfigDetail.AddToClassList("ctx-sb-config-detail");
            _sbConfigDetail.style.whiteSpace = WhiteSpace.Normal;
            _sbConfigDetail.style.display = DisplayStyle.None;
            cfgSection.Add(_sbConfigDetail);
            _sceneBuilderPanel.Add(cfgSection);

            // ── 环境状态 ──
            var envSection = BuildInfoSection("\u73AF\u5883\u72B6\u6001");
            _sbStateRenderLabel = new Label("");
            _sbStateRenderLabel.AddToClassList("ctx-info-value");
            envSection.Add(_sbStateRenderLabel);
            _sceneBuilderPanel.Add(envSection);

            // ── 操作提示 ──
            var hintSection = BuildInfoSection("\u64CD\u4F5C\u63D0\u793A");
            _sbHint = new Label("\u4ECE\u5DE6\u4FA7\u9009\u62E9\u573A\u666F\u6A21\u677F\u5F00\u59CB\u914D\u7F6E");
            _sbHint.AddToClassList("ctx-info-value");
            _sbHint.style.whiteSpace = WhiteSpace.Normal;
            hintSection.Add(_sbHint);
            _sceneBuilderPanel.Add(hintSection);

            parent.Add(_sceneBuilderPanel);
        }

        private void UpdateSBFromContext(SelectionContextEvent evt)
        {
            if (_sbTemplateName != null)
                _sbTemplateName.text = evt.Title ?? "\u672A\u9009\u62E9\u6A21\u677F";
            if (_sbTemplateSubtitle != null)
                _sbTemplateSubtitle.text = evt.Subtitle ?? "";

            if (_sbConfigDetail != null)
            {
                _sbConfigDetail.text = evt.Detail ?? "";
                _sbConfigDetail.style.display = string.IsNullOrEmpty(evt.Detail)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }

            // ActionHint 包含操作提示 + State Render 状态（由 SceneController 拼接）
            if (evt.ActionHint != null)
            {
                var lines = evt.ActionHint.Split('\n');
                if (_sbHint != null)
                    _sbHint.text = lines.Length > 0 ? lines[0] : "";
                if (_sbStateRenderLabel != null && lines.Length > 1)
                {
                    _sbStateRenderLabel.text = lines[1];
                    _sbStateRenderLabel.RemoveFromClassList("ctx-dep-ok");
                    _sbStateRenderLabel.RemoveFromClassList("ctx-dep-warning");
                    _sbStateRenderLabel.AddToClassList(
                        lines[1].Contains("\u2713") ? "ctx-dep-ok" : "ctx-dep-warning");
                }
            }
        }

        private void RefreshSBStateRender()
        {
            if (_sbStateRenderLabel == null || _state == null) return;
            bool available = _state.StateRenderHealth.Value == PackageHealth.Installed;
            _sbStateRenderLabel.text = available
                ? "\u2713 State Render System \u5DF2\u5C31\u7EEA\uFF0C\u6240\u6709\u6548\u679C\u53EF\u7528"
                : "\u26A0 State Render System \u672A\u5B89\u88C5\uFF0C\u9AD8\u7EA7\u6548\u679C\u964D\u7EA7";
            _sbStateRenderLabel.RemoveFromClassList("ctx-dep-ok");
            _sbStateRenderLabel.RemoveFromClassList("ctx-dep-warning");
            _sbStateRenderLabel.AddToClassList(available ? "ctx-dep-ok" : "ctx-dep-warning");
        }

        // ════════════════════════════════════════════════════════════════
        // 面板 D：VehicleSetup — 零件上下文
        // ════════════════════════════════════════════════════════════════

        private void BuildVehicleSetupPanel(VisualElement parent)
        {
            _vehicleSetupPanel = new VisualElement();
            _vehicleSetupPanel.AddToClassList("ctx-panel");
            _vehicleSetupPanel.style.display = DisplayStyle.None;

            var header = new Label("\u8F66\u8F86\u96F6\u4EF6");
            header.AddToClassList("ctx-panel-header");
            _vehicleSetupPanel.Add(header);

            // ── 零件名称 ──
            var nameSection = BuildInfoSection("\u5F53\u524D\u96F6\u4EF6");
            _vsPartName = new Label("\u672A\u9009\u62E9\u96F6\u4EF6");
            _vsPartName.AddToClassList("ctx-vs-part-name");
            nameSection.Add(_vsPartName);

            _vsPartSubtitle = new Label("");
            _vsPartSubtitle.AddToClassList("ctx-info-value");
            nameSection.Add(_vsPartSubtitle);
            _vehicleSetupPanel.Add(nameSection);

            // ── 状态 ──
            var statusSection = BuildInfoSection("\u5DE5\u4F5C\u6D41\u72B6\u6001");
            _vsStatusLabel = new Label("");
            _vsStatusLabel.AddToClassList("ctx-info-value");
            _vsStatusLabel.AddToClassList("ctx-vs-status");
            statusSection.Add(_vsStatusLabel);
            _vehicleSetupPanel.Add(statusSection);

            // ── 详情 ──
            var detailSection = BuildInfoSection("\u8BE6\u7EC6\u4FE1\u606F");
            _vsDetailLabel = new Label("");
            _vsDetailLabel.AddToClassList("ctx-vs-detail");
            _vsDetailLabel.style.whiteSpace = WhiteSpace.Normal;
            _vsDetailLabel.style.display = DisplayStyle.None;
            detailSection.Add(_vsDetailLabel);
            _vehicleSetupPanel.Add(detailSection);

            // ── 操作提示 ──
            var hintSection = BuildInfoSection("\u64CD\u4F5C\u63D0\u793A");
            _vsHint = new Label("\u5BFC\u5165 FBX \u6587\u4EF6\u4EE5\u5F00\u59CB\u8F66\u8F86\u8BBE\u7F6E");
            _vsHint.AddToClassList("ctx-info-value");
            _vsHint.style.whiteSpace = WhiteSpace.Normal;
            hintSection.Add(_vsHint);
            _vehicleSetupPanel.Add(hintSection);

            parent.Add(_vehicleSetupPanel);
        }

        private void UpdateVSFromContext(SelectionContextEvent evt)
        {
            if (_vsPartName != null)
                _vsPartName.text = evt.Title ?? "\u672A\u9009\u62E9\u96F6\u4EF6";
            if (_vsPartSubtitle != null)
                _vsPartSubtitle.text = evt.Subtitle ?? "";

            if (_vsStatusLabel != null)
            {
                // Subtitle 包含类型 + 状态标签
                _vsStatusLabel.text = evt.Subtitle ?? "";
                _vsStatusLabel.RemoveFromClassList("ctx-dep-ok");
                _vsStatusLabel.RemoveFromClassList("ctx-dep-warning");
                bool isReady = evt.Subtitle != null && evt.Subtitle.Contains("\u2713");
                _vsStatusLabel.AddToClassList(isReady ? "ctx-dep-ok" : "ctx-dep-warning");
            }

            if (_vsDetailLabel != null)
            {
                _vsDetailLabel.text = evt.Detail ?? "";
                _vsDetailLabel.style.display = string.IsNullOrEmpty(evt.Detail)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_vsHint != null)
                _vsHint.text = evt.ActionHint ?? "";
        }

        // ════════════════════════════════════════════════════════════════
        // 面板 E：通用信息面板（Home 等）
        // ════════════════════════════════════════════════════════════════

        private void BuildGenericPanel(VisualElement parent)
        {
            _genericPanel = new VisualElement();
            _genericPanel.AddToClassList("ctx-panel");
            _genericPanel.style.display = DisplayStyle.None;

            var header = new Label("\u5F53\u524D\u4E0A\u4E0B\u6587");
            header.AddToClassList("ctx-panel-header");
            _genericPanel.Add(header);

            _genPreview = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _genPreview.AddToClassList("ctx-gen-preview");
            _genPreview.style.display = DisplayStyle.None;
            _genericPanel.Add(_genPreview);

            _genTitle = new Label("");
            _genTitle.AddToClassList("ctx-gen-title");
            _genericPanel.Add(_genTitle);

            _genSubtitle = new Label("");
            _genSubtitle.AddToClassList("ctx-gen-subtitle");
            _genericPanel.Add(_genSubtitle);

            _genDetail = new Label("");
            _genDetail.AddToClassList("ctx-gen-detail");
            _genericPanel.Add(_genDetail);

            _genHint = new Label("\u9009\u62E9\u5185\u5BB9\u4EE5\u67E5\u770B\u8BE6\u60C5");
            _genHint.AddToClassList("ctx-gen-hint");
            _genericPanel.Add(_genHint);

            parent.Add(_genericPanel);
        }

        // ════════════════════════════════════════════════════════════════
        // Asset Browser 刷新逻辑
        // ════════════════════════════════════════════════════════════════

        private void RefreshStep1(UnityEngine.Object obj)
        {
            _pendingMaterial = null;
            EditorApplication.update -= PollMaterialPreview;

            if (obj == null || obj is not GameObject go)
            {
                SetStepState(_step1Indicator, _step1Content, _step1Empty, false, "1");
                if (_targetName != null) _targetName.text = "";
                if (_targetRenderer != null) _targetRenderer.text = "";
                if (_targetMatName != null) _targetMatName.text = "";
                if (_targetMatPreview != null)
                {
                    _targetMatPreview.image = null;
                    _targetMatPreview.style.display = DisplayStyle.None;
                }
                RefreshStep3();
                return;
            }

            var renderer = go.GetComponent<Renderer>();
            if (_targetName != null) _targetName.text = go.name;
            if (_targetRenderer != null)
                _targetRenderer.text = renderer != null ? renderer.GetType().Name : "\u65E0 Renderer";

            var mat = renderer != null ? renderer.sharedMaterial : null;
            if (mat != null)
            {
                if (_targetMatName != null) _targetMatName.text = mat.name;
                ShowMatPreview(PreviewService.GetBestThumbnail(mat));
            }
            else
            {
                if (_targetMatName != null) _targetMatName.text = "\u65E0\u6750\u8D28";
                if (_targetMatPreview != null)
                {
                    _targetMatPreview.image = null;
                    _targetMatPreview.style.display = DisplayStyle.None;
                }
            }

            SetStepState(_step1Indicator, _step1Content, _step1Empty,
                renderer != null, "\u2713");
            RefreshStep3();
        }

        private void RefreshStep2Asset(AssetEntry asset)
        {
            if (asset == null)
            {
                SetStepState(_step2Indicator, _step2Content, _step2Empty, false, "2");
                RefreshStep3();
                return;
            }

            if (_assetName != null) _assetName.text = asset.DisplayName;
            if (_assetBadge != null) _assetBadge.text = KindToLabel(asset.Kind);
            if (_assetPath != null) _assetPath.text = asset.Path ?? "";

            if (_assetThumb != null)
            {
                _assetThumb.image = asset.UnityObject != null
                    ? PreviewService.GetBestThumbnail(asset.UnityObject)
                    : null;
            }

            SetStepState(_step2Indicator, _step2Content, _step2Empty, true, "\u2713");
            RefreshStep3();
        }

        private void RefreshStep3()
        {
            if (_applyBtn == null || _actionController == null) return;
            var (canApply, reason) = _actionController.QueryApplyState();

            _applyBtn.SetEnabled(canApply);
            _applyBtn.tooltip = reason;
            if (_reasonLabel != null) _reasonLabel.text = reason;

            bool step3Active = canApply;
            SetStepState(_step3Indicator, _step3Content, _step3Empty,
                step3Active, step3Active ? "\u2713" : "3");
        }

        // ════════════════════════════════════════════════════════════════
        // BatchReplace 面板刷新
        // ════════════════════════════════════════════════════════════════

        private void RefreshBRTarget()
        {
            if (_activeContext != "BatchReplace") return;
            // 此方法被 UnitySelection.Changed 触发，目标信息来自事件
        }

        private void RefreshBRStatus()
        {
            if (_activeContext != "BatchReplace") return;
            if (_brStatusLabel == null) return;

            // 通过 SelectionContextEvent 刷新（见 OnContextChanged）
        }

        // ════════════════════════════════════════════════════════════════
        // 跨模式信号处理
        // ════════════════════════════════════════════════════════════════

        private void OnContextChanged(SelectionContextEvent evt)
        {
            if (evt.SourceMode == "VehicleSetup" && _activeContext == "VehicleSetup")
            {
                UpdateVSFromContext(evt);
            }
            else if (evt.SourceMode == "SceneBuilder" && _activeContext == "SceneBuilder")
            {
                UpdateSBFromContext(evt);
            }
            else if (evt.SourceMode == "BatchReplace" && _activeContext == "BatchReplace")
            {
                // 更新 BatchReplace 面板
                UpdateBRFromContext(evt);
            }
            else if (_activeContext == "AssetBrowser")
            {
                // Asset Browser 模式下，更新 Step 2
                if (_assetName != null) _assetName.text = evt.Title ?? "";
                if (_assetBadge != null) _assetBadge.text = evt.SourceMode switch
                {
                    "AssetBrowser" => "\u8D44\u4EA7",
                    "BatchReplace" => "\u6750\u8D28",
                    "SceneBuilder" => "\u573A\u666F",
                    "VehicleSetup" => "\u96F6\u4EF6",
                    _ => "\u9009\u4E2D\u9879",
                };
                if (_assetPath != null) _assetPath.text = evt.Subtitle ?? "";
                if (_assetThumb != null)
                {
                    _assetThumb.image = evt.Preview;
                    _assetThumb.style.display = evt.Preview != null ? DisplayStyle.Flex : DisplayStyle.None;
                }
                SetStepState(_step2Indicator, _step2Content, _step2Empty, true, "\u2713");
                RefreshStep3();
            }
            else if (_activeContext == "Generic")
            {
                // 通用面板
                UpdateGenericFromContext(evt);
            }
        }

        private void OnContextCleared(SelectionContextClearedEvent evt)
        {
            if (_activeContext == "AssetBrowser")
            {
                SetStepState(_step2Indicator, _step2Content, _step2Empty, false, "2");
                if (_step2Empty != null)
                    _step2Empty.text = evt.EmptyMessage ?? "\u4ECE\u4E2D\u95F4\u5217\u8868\u70B9\u51FB\u4E00\u4E2A\u8D44\u4EA7";
                RefreshStep3();
            }
            else if (_activeContext == "BatchReplace")
            {
                if (_brTargetLabel != null) _brTargetLabel.text = "\u672A\u9009\u4E2D";
                if (_brStatusLabel != null) _brStatusLabel.text = evt.EmptyMessage ?? "\u7B49\u5F85\u9009\u62E9\u76EE\u6807\u548C\u5019\u9009\u6750\u8D28";
            }
            else if (_activeContext == "SceneBuilder")
            {
                if (_sbTemplateName != null) _sbTemplateName.text = "\u672A\u9009\u62E9\u6A21\u677F";
                if (_sbTemplateSubtitle != null) _sbTemplateSubtitle.text = "";
                if (_sbConfigDetail != null) { _sbConfigDetail.text = ""; _sbConfigDetail.style.display = DisplayStyle.None; }
                if (_sbHint != null) _sbHint.text = evt.EmptyMessage ?? "\u4ECE\u5DE6\u4FA7\u9009\u62E9\u573A\u666F\u6A21\u677F\u5F00\u59CB\u914D\u7F6E";
            }
            else if (_activeContext == "VehicleSetup")
            {
                if (_vsPartName != null) _vsPartName.text = "\u672A\u9009\u62E9\u96F6\u4EF6";
                if (_vsPartSubtitle != null) _vsPartSubtitle.text = "";
                if (_vsDetailLabel != null) { _vsDetailLabel.text = ""; _vsDetailLabel.style.display = DisplayStyle.None; }
                if (_vsHint != null) _vsHint.text = evt.EmptyMessage ?? "\u4ECE\u5DE6\u4FA7\u96F6\u4EF6\u5217\u8868\u9009\u62E9\u4E00\u4E2A\u96F6\u4EF6";
            }
            else if (_activeContext == "Generic")
            {
                if (_genTitle != null) _genTitle.text = "";
                if (_genSubtitle != null) _genSubtitle.text = "";
                if (_genDetail != null) _genDetail.text = "";
                if (_genHint != null) _genHint.text = evt.EmptyMessage ?? "\u9009\u62E9\u5185\u5BB9\u4EE5\u67E5\u770B\u8BE6\u60C5";
                if (_genPreview != null) _genPreview.style.display = DisplayStyle.None;
            }
        }

        private void UpdateBRFromContext(SelectionContextEvent evt)
        {
            // 解析 BatchReplace 的 SelectionContextEvent
            // Title = 目标对象名, Subtitle = "当前 → 新" 或状态描述
            // Detail = 多行详情, ActionHint = 操作提示

            if (_brTargetLabel != null)
                _brTargetLabel.text = evt.Title ?? "\u672A\u9009\u4E2D";

            // 从 Detail 解析材质名和影响数量
            if (evt.Detail != null)
            {
                var lines = evt.Detail.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("\u53D7\u5F71\u54CD\u5BF9\u8C61"))
                    {
                        if (_brImpactLabel != null)
                            _brImpactLabel.text = line.Replace("\u53D7\u5F71\u54CD\u5BF9\u8C61\uFF1A", "").Trim() + " \u4E2A\u5BF9\u8C61";
                    }
                    else if (line.StartsWith("\u5F53\u524D\u6750\u8D28"))
                    {
                        var matName = line.Replace("\u5F53\u524D\u6750\u8D28\uFF1A", "").Trim();
                        if (_brCurrentMatLabel != null) _brCurrentMatLabel.text = matName;
                    }
                    else if (line.StartsWith("\u66FF\u6362\u4E3A"))
                    {
                        var matName = line.Replace("\u66FF\u6362\u4E3A\uFF1A", "").Trim();
                        if (_brNewMatLabel != null) _brNewMatLabel.text = matName;
                    }
                }
            }

            // 预览图更新
            if (_brNewMatThumb != null)
            {
                _brNewMatThumb.image = evt.Preview;
                _brNewMatThumb.style.display = evt.Preview != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 操作状态
            if (_brStatusLabel != null)
                _brStatusLabel.text = evt.ActionHint ?? "";
        }

        private void UpdateGenericFromContext(SelectionContextEvent evt)
        {
            if (_genTitle != null) _genTitle.text = evt.Title ?? "";
            if (_genSubtitle != null) _genSubtitle.text = evt.Subtitle ?? "";
            if (_genDetail != null)
            {
                _genDetail.text = evt.Detail ?? "";
                _genDetail.style.display = string.IsNullOrEmpty(evt.Detail) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_genHint != null) _genHint.text = evt.ActionHint ?? "";
            if (_genPreview != null)
            {
                _genPreview.image = evt.Preview;
                _genPreview.style.display = evt.Preview != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 辅助
        // ════════════════════════════════════════════════════════════════

        private void ShowMatPreview(Texture tex)
        {
            if (_targetMatPreview == null) return;
            _targetMatPreview.image = tex;
            _targetMatPreview.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void PollMaterialPreview()
        {
            if (_pendingMaterial == null)
            {
                EditorApplication.update -= PollMaterialPreview;
                return;
            }
            var preview = PreviewService.GetResolvedPreviewOrNull(_pendingMaterial);
            if (preview != null)
            {
                ShowMatPreview(preview);
                _pendingMaterial = null;
                EditorApplication.update -= PollMaterialPreview;
            }
        }

        private static void SetStepState(Label indicator, VisualElement content,
            Label empty, bool done, string indicatorText)
        {
            if (indicator != null)
            {
                indicator.text = indicatorText;
                indicator.RemoveFromClassList("act-step-pending");
                indicator.RemoveFromClassList("act-step-done");
                indicator.AddToClassList(done ? "act-step-done" : "act-step-pending");
            }

            if (content != null)
                content.style.display = done ? DisplayStyle.Flex : DisplayStyle.None;
            if (empty != null)
                empty.style.display = done ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null)
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement BuildInfoSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("ctx-section");

            var label = new Label(title);
            label.AddToClassList("ctx-section-title");
            section.Add(label);

            return section;
        }

        // ── IInspectorView ──

        public void ShowConfirmDialog(string title, string message, Action onConfirm)
        {
            if (EditorUtility.DisplayDialog(title, message, "\u786E\u8BA4", "\u53D6\u6D88"))
                onConfirm?.Invoke();
        }

        public void FlashActionButton(string actionName) { /* Phase 2 */ }

        private static string KindToLabel(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "\u6750\u8D28",
                AssetKind.Texture  => "\u8D34\u56FE",
                AssetKind.Prefab   => "\u9884\u5236\u4F53",
                AssetKind.Shader   => "\u7740\u8272\u5668",
                AssetKind.Model    => "\u6A21\u578B",
                AssetKind.Scene    => "\u573A\u666F",
                AssetKind.Fx       => "\u7279\u6548",
                _                  => "\u8D44\u4EA7",
            };
        }
    }
}
