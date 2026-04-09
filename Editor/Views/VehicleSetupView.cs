using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 车辆设置工作区。
    /// 左：按状态分组的零件树  |  中：3D 预览  |  右：绑定 + 修复 + 导出面板
    ///
    /// 所有业务逻辑（扫描、验证、修复、导出、上下文发布）委托给 VehicleSetupController。
    /// View 只负责 UI 构建、用户交互、3D 预览渲染。
    /// </summary>
    public sealed class VehicleSetupView
    {
        private readonly VisualElement _root;
        private readonly WorkspaceState _workspaceState;
        private VehicleSetupController _controller;

        // UI 缓存
        private VisualElement _partList;
        private VisualElement _previewArea;
        private Label _previewLabel;
        private VisualElement _bindingPanel;
        private Label _vehicleNameLabel;
        private Label _validationLabel;
        private Label _partCountLabel;
        private VisualElement _slotContainer;
        private Button _exportBtn;

        // 批量操作按钮
        private Button _autoFixAllBtn;

        // 进度指示器
        private Label _step1Indicator;
        private Label _step2Indicator;
        private Label _step3Indicator;
        private Label _step1Label;
        private Label _step2Label;
        private Label _step3Label;

        // ── 3D 交互预览 ──
        private PreviewRenderUtility _previewUtility;
        private GameObject _previewInstance;
        private IMGUIContainer _imguiPreview;
        private float _orbitYaw = 135f;
        private float _orbitPitch = 20f;
        private float _orbitDistance = 5f;
        private Vector3 _orbitPivot = Vector3.zero;
        private Vector2 _lastMousePos;
        private bool _isDragging;

        // 高亮材质缓存
        private Material _highlightMaterial;
        private Material _dimMaterial;
        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
        private readonly Dictionary<string, Renderer> _previewRendererMap = new();
        // 高亮材质复用池：避免每次选中时 new Material 泄漏
        private readonly Dictionary<Renderer, Material[]> _highlightCache = new();
        private Renderer _lastHighlightedRenderer;

        public VehicleSetupView(VisualElement root, WorkspaceState state)
        {
            _root = root;
            _workspaceState = state;
        }

        /// <summary>
        /// 释放 PreviewRenderUtility 及相关资源。
        /// 必须在 EditorWindow.OnDisable 中调用，否则 assembly reload 时会泄漏。
        /// </summary>
        public void Dispose()
        {
            CleanupPreview();
        }

        /// <summary>
        /// 绑定到控制器。View 从 controller.SetupState 读取响应式状态。
        /// </summary>
        public void Bind(VehicleSetupController controller)
        {
            if (_root == null) return;
            _controller = controller;
            _root.Clear();
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.flexGrow = 1;

            BuildLeftPanel();
            BuildCenterPanel();
            BuildRightPanel();

            // ── 响应式绑定 ──
            var vs = _controller.SetupState;
            vs.Parts.Changed += (_, __) => RebuildPartList();
            vs.SelectedPart.Changed += (_, part) =>
            {
                RefreshBindingPanel(part);
                HighlightPartInPreview(part);
            };
            vs.ValidationSummary.Changed += (_, msg) => { if (_validationLabel != null) _validationLabel.text = msg; };
            vs.VehicleName.Changed += (_, name) => { if (_vehicleNameLabel != null) _vehicleNameLabel.text = name; };
        }

        // ════════════════════════════════════════════════════════════════
        // 左面板：按状态分组的零件树
        // ════════════════════════════════════════════════════════════════
        private void BuildLeftPanel()
        {
            var panel = CreatePanel("ws-left-panel");
            panel.style.width = 260;
            panel.style.minWidth = 200;

            // ── 进度指示器 ──
            var progressBar = new VisualElement();
            progressBar.AddToClassList("ws-progress-bar");

            _step1Indicator = CreateStepIndicator("1", true);
            _step1Label = CreateStepLabel("\u5BFC\u5165", true);
            _step2Indicator = CreateStepIndicator("2", false);
            _step2Label = CreateStepLabel("\u89E3\u6790", false);
            _step3Indicator = CreateStepIndicator("3", false);
            _step3Label = CreateStepLabel("\u5BFC\u51FA", false);

            var connector1 = CreateStepConnector();
            var connector2 = CreateStepConnector();

            progressBar.Add(CreateStepNode(_step1Indicator, _step1Label));
            progressBar.Add(connector1);
            progressBar.Add(CreateStepNode(_step2Indicator, _step2Label));
            progressBar.Add(connector2);
            progressBar.Add(CreateStepNode(_step3Indicator, _step3Label));

            panel.Add(progressBar);

            // 标题行
            var header = new VisualElement();
            header.AddToClassList("ws-panel-header");
            header.Add(MakeTitle("\u96F6\u4EF6\u5C42\u7EA7"));
            _partCountLabel = new Label("0 \u4E2A\u96F6\u4EF6");
            _partCountLabel.AddToClassList("ws-panel-badge");
            header.Add(_partCountLabel);
            panel.Add(header);

            // 导入按钮
            var importBtn = new Button(OnImportClicked) { text = "\u5BFC\u5165\u8F66\u8F86\u6A21\u578B" };
            importBtn.AddToClassList("ws-action-btn-primary");
            panel.Add(importBtn);

            // 批量修复按钮
            _autoFixAllBtn = new Button(OnAutoFixAllClicked) { text = "\u26A1 \u5168\u90E8\u81EA\u52A8\u4FEE\u590D" };
            _autoFixAllBtn.AddToClassList("vs-action-btn-fix-all");
            _autoFixAllBtn.SetEnabled(false);
            panel.Add(_autoFixAllBtn);

            // 零件列表（分组）
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _partList = new VisualElement();
            scroll.Add(_partList);
            panel.Add(scroll);

            _root.Add(panel);
        }

        // ════════════════════════════════════════════════════════════════
        // 中面板：3D 交互预览
        // ════════════════════════════════════════════════════════════════
        private void BuildCenterPanel()
        {
            var panel = CreatePanel("ws-center-panel");
            panel.style.flexGrow = 1;

            var header = new VisualElement();
            header.AddToClassList("ws-panel-header");
            _vehicleNameLabel = new Label("\u672A\u5BFC\u5165");
            _vehicleNameLabel.AddToClassList("ws-panel-title");
            header.Add(_vehicleNameLabel);

            var hintLabel = new Label("\u5DE6\u952E\u62D6\u62FD\u65CB\u8F6C  \u2022  \u6EDA\u8F6E\u7F29\u653E  \u2022  \u4E2D\u952E\u62D6\u62FD\u5E73\u79FB");
            hintLabel.AddToClassList("ws-preview-hint");
            header.Add(hintLabel);

            var resetBtn = new Button(() =>
            {
                HighlightPartInPreview(null);
                ResetCameraToFullView();
                _controller?.SelectPart(null);
            }) { text = "\u21BA \u663E\u793A\u5168\u8F66" };
            resetBtn.AddToClassList("ws-action-btn-muted");
            header.Add(resetBtn);

            panel.Add(header);

            _previewArea = new VisualElement();
            _previewArea.AddToClassList("ws-preview-area");

            _previewLabel = new Label("\u70B9\u51FB\u5DE6\u4FA7\u300C\u5BFC\u5165\u8F66\u8F86\u6A21\u578B\u300D\u6309\u94AE\n\u652F\u6301 FBX / Prefab / GLB \u683C\u5F0F");
            _previewLabel.AddToClassList("ws-preview-placeholder");
            _previewArea.Add(_previewLabel);

            panel.Add(_previewArea);
            _root.Add(panel);
        }

        // ════════════════════════════════════════════════════════════════
        // 右面板：绑定 + 修复动作 + 导出
        // ════════════════════════════════════════════════════════════════
        private void BuildRightPanel()
        {
            var panel = CreatePanel("ws-right-panel");
            panel.style.width = 300;
            panel.style.minWidth = 220;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            // ── 绑定区域 ──
            scroll.Add(MakeSectionTitle("\u96F6\u4EF6\u7ED1\u5B9A"));
            _bindingPanel = new VisualElement();
            _bindingPanel.Add(CreateHint("\u8BF7\u4ECE\u5DE6\u4FA7\u9009\u62E9\u4E00\u4E2A\u96F6\u4EF6"));
            scroll.Add(_bindingPanel);

            // ── 材质槽 ──
            scroll.Add(MakeSectionTitle("\u6750\u8D28\u69FD\u4F4D"));
            _slotContainer = new VisualElement();
            _slotContainer.Add(CreateHint("\u9009\u62E9\u96F6\u4EF6\u540E\u663E\u793A\u6750\u8D28\u69FD\u4F4D"));
            scroll.Add(_slotContainer);

            // ── 命名 & 验证摘要 ──
            scroll.Add(MakeSectionTitle("\u9A8C\u8BC1\u6458\u8981"));
            _validationLabel = new Label("\u5BFC\u5165\u8F66\u8F86\u6A21\u578B\u540E\u5C06\u81EA\u52A8\u68C0\u67E5\u96F6\u4EF6\u547D\u540D\u89C4\u8303\u4E0E\u7C7B\u578B\u8BC6\u522B");
            _validationLabel.AddToClassList("ws-hint");
            _validationLabel.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(_validationLabel);

            // ── 导出按钮 ──
            scroll.Add(MakeSectionTitle("\u5BFC\u51FA"));
            _exportBtn = new Button(OnExportClicked) { text = "\u751F\u6210\u8F66\u8F86 Schema JSON" };
            _exportBtn.AddToClassList("ws-action-btn-primary");
            _exportBtn.SetEnabled(false);
            scroll.Add(_exportBtn);

            panel.Add(scroll);
            _root.Add(panel);
        }

        // ════════════════════════════════════════════════════════════════
        // 操作 — 委托给 Controller
        // ════════════════════════════════════════════════════════════════

        private void OnImportClicked()
        {
            var path = EditorUtility.OpenFilePanel("\u9009\u62E9\u8F66\u8F86\u6A21\u578B", "Assets", "fbx,prefab,glb");
            if (string.IsNullOrEmpty(path)) return;

            // 转换为相对路径
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            if (_controller.ImportVehicle(path))
            {
                // 导入成功 → 启动 3D 预览
                var prefab = _controller.ImportedPrefab;
                if (prefab != null)
                    SetupInteractivePreview(prefab);

                _exportBtn?.SetEnabled(true);
                SetProgressStep(2);
            }
        }

        private void OnAutoFixAllClicked()
        {
            if (_controller == null) return;
            int count = _controller.AutoFixAll();
            if (count > 0)
                _workspaceState.StatusMessage.Value = $"\u5DF2\u6279\u91CF\u4FEE\u590D {count} \u4E2A\u96F6\u4EF6\u547D\u540D";
        }

        private void OnExportClicked()
        {
            if (_controller == null) return;
            if (_controller.ExportSchema())
                SetProgressStep(3);
        }

        // ════════════════════════════════════════════════════════════════
        // 分组零件列表
        // ════════════════════════════════════════════════════════════════

        private void RebuildPartList()
        {
            using var _t = PerfTrace.Begin("VehicleSetupView.RebuildPartList");
            if (_partList == null || _controller == null) return;
            _partList.Clear();

            var vs = _controller.SetupState;
            var parts = vs.Parts.Value;
            if (_partCountLabel != null)
                _partCountLabel.text = $"{parts.Count} \u4E2A\u96F6\u4EF6";

            // 按 PartStatus 分组
            var groups = new[]
            {
                (PartStatus.NeedsFix,     "\u26A0 \u5EFA\u8BAE\u4FEE\u590D",  "vs-group-needsfix"),
                (PartStatus.Unrecognized, "? \u672A\u8BC6\u522B",        "vs-group-unrecognized"),
                (PartStatus.Ready,        "\u2713 \u5DF2\u5C31\u7EEA",        "vs-group-ready"),
                (PartStatus.Ignored,      "\u2014 \u5DF2\u5FFD\u7565",        "vs-group-ignored"),
            };

            foreach (var (status, title, cssClass) in groups)
            {
                var groupParts = parts.Where(p => p.Status == status).ToList();
                if (groupParts.Count == 0) continue;

                // 组头
                var groupHeader = new VisualElement();
                groupHeader.AddToClassList("vs-group-header");
                groupHeader.AddToClassList(cssClass);

                var groupTitle = new Label($"{title}  ({groupParts.Count})");
                groupTitle.AddToClassList("vs-group-title");
                groupHeader.Add(groupTitle);

                _partList.Add(groupHeader);

                // 组内零件
                foreach (var part in groupParts)
                {
                    var item = CreatePartListItem(part);
                    _partList.Add(item);
                }
            }

            // 更新批量修复按钮
            var (_, needsFix, unrecognized, _) = _controller.GetStatusCounts();
            _autoFixAllBtn?.SetEnabled(needsFix > 0);
        }

        private VisualElement CreatePartListItem(VehiclePart part)
        {
            var item = new VisualElement();
            item.AddToClassList("ws-list-item");
            item.userData = part;

            // 状态徽章
            var statusBadge = new Label(StatusIcon(part.Status));
            statusBadge.AddToClassList("vs-status-badge");
            statusBadge.AddToClassList($"vs-status-{part.Status.ToString().ToLowerInvariant()}");
            item.Add(statusBadge);

            // 图标
            var icon = new VisualElement();
            icon.AddToClassList("ws-part-icon");
            icon.AddToClassList($"ws-part-{part.PartType.ToString().ToLowerInvariant()}");
            item.Add(icon);

            // 文字列
            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.overflow = Overflow.Hidden;

            var nameLabel = new Label(part.Name);
            nameLabel.AddToClassList("ws-list-item-name");

            // 无意义名称显示建议
            if (part.IsMeaninglessName && !string.IsNullOrEmpty(part.SuggestedName))
            {
                nameLabel.AddToClassList("vs-meaningless-name");
            }

            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.alignItems = Align.Center;

            var typeLabel = new Label(VehicleSetupController.PartTypeToLabel(part.PartType));
            typeLabel.AddToClassList("ws-list-item-meta");
            metaRow.Add(typeLabel);

            // NeedsFix 显示建议名称
            if (part.Status == PartStatus.NeedsFix && !string.IsNullOrEmpty(part.SuggestedName))
            {
                var suggestLabel = new Label($"\u2192 {part.SuggestedName}");
                suggestLabel.AddToClassList("vs-suggest-inline");
                metaRow.Add(suggestLabel);
            }

            // Unrecognized 显示提示
            if (part.Status == PartStatus.Unrecognized)
            {
                var hintLabel = new Label("\u2190 \u8BF7\u624B\u52A8\u7ED1\u5B9A\u7C7B\u578B");
                hintLabel.AddToClassList("vs-suggest-inline");
                hintLabel.AddToClassList("vs-unrecognized-hint");
                metaRow.Add(hintLabel);
            }

            textCol.Add(nameLabel);
            textCol.Add(metaRow);
            item.Add(textCol);

            // 点击选中
            item.RegisterCallback<ClickEvent>(_ =>
            {
                foreach (var child in _partList.Children())
                    child.RemoveFromClassList("selected");
                item.AddToClassList("selected");
                _controller.SelectPart(part);
                TrySelectInScene(part);
            });

            return item;
        }

        // ════════════════════════════════════════════════════════════════
        // 右面板刷新
        // ════════════════════════════════════════════════════════════════

        private void RefreshBindingPanel(VehiclePart part)
        {
            if (_bindingPanel == null) return;
            _bindingPanel.Clear();

            if (part == null)
            {
                _bindingPanel.Add(CreateHint("\u8BF7\u4ECE\u5DE6\u4FA7\u9009\u62E9\u4E00\u4E2A\u96F6\u4EF6"));
                _slotContainer?.Clear();
                _slotContainer?.Add(CreateHint("\u9009\u62E9\u96F6\u4EF6\u540E\u663E\u793A\u6750\u8D28\u69FD\u4F4D"));
                return;
            }

            // ── 基本信息 ──
            _bindingPanel.Add(CreateFormRow("\u540D\u79F0", part.Name));
            _bindingPanel.Add(CreateFormRow("\u8DEF\u5F84", part.ObjectPath));

            // ── 状态行 ──
            var statusRow = new VisualElement();
            statusRow.AddToClassList("ws-form-row");
            statusRow.Add(CreateFormLabel("\u72B6\u6001"));
            var statusLabel = new Label(VehicleSetupController.StatusToLabel(part.Status));
            statusLabel.AddToClassList("ws-form-value");
            statusLabel.AddToClassList($"vs-status-text-{part.Status.ToString().ToLowerInvariant()}");
            statusRow.Add(statusLabel);
            _bindingPanel.Add(statusRow);

            // ── 类型下拉 ──
            var typeRow = new VisualElement();
            typeRow.AddToClassList("ws-form-row");
            typeRow.Add(CreateFormLabel("\u7C7B\u578B"));

            var typeNames = System.Enum.GetNames(typeof(VehiclePartType));
            var dropdown = new PopupField<string>(
                typeNames.ToList(),
                (int)part.PartType);
            dropdown.AddToClassList("ws-form-dropdown");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (System.Enum.TryParse<VehiclePartType>(evt.newValue, out var newType))
                    _controller.BindPartType(part, newType);
            });
            typeRow.Add(dropdown);
            _bindingPanel.Add(typeRow);

            _bindingPanel.Add(CreateFormRow("\u7ED1\u5B9A\u5BF9\u8C61", part.BoundGameObject));

            // ── 命名状态 + 建议 ──
            if (part.IsMeaninglessName)
            {
                var warnRow = new VisualElement();
                warnRow.AddToClassList("vs-warn-row");
                var warnIcon = new Label("\u26A0");
                warnIcon.AddToClassList("vs-warn-icon");
                var warnText = new Label("\u6B64\u540D\u79F0\u7591\u4F3C\u7531 DCC \u5DE5\u5177\u81EA\u52A8\u751F\u6210\uFF0C\u5EFA\u8BAE\u5148\u7ED1\u5B9A\u7C7B\u578B\u518D\u6267\u884C\u81EA\u52A8\u4FEE\u590D");
                warnText.AddToClassList("vs-warn-text");
                warnText.style.whiteSpace = WhiteSpace.Normal;
                warnRow.Add(warnIcon);
                warnRow.Add(warnText);
                _bindingPanel.Add(warnRow);
            }

            if (!string.IsNullOrEmpty(part.SuggestedName))
            {
                var suggestRow = new VisualElement();
                suggestRow.AddToClassList("ws-suggestion-row");
                var suggestIcon = new Label("\u2192");
                suggestIcon.AddToClassList("ws-suggestion-icon");
                var suggestText = new Label($"\u5EFA\u8BAE: {part.SuggestedName}");
                suggestText.AddToClassList("ws-suggestion-text");
                suggestText.style.whiteSpace = WhiteSpace.Normal;
                suggestRow.Add(suggestIcon);
                suggestRow.Add(suggestText);
                _bindingPanel.Add(suggestRow);
            }

            // ── 修复动作按钮 ──
            var actionBar = new VisualElement();
            actionBar.AddToClassList("vs-action-bar");

            if (part.Status == PartStatus.NeedsFix && part.PartType != VehiclePartType.Unknown)
            {
                var fixBtn = new Button(() =>
                {
                    _controller.AutoFixName(part);
                }) { text = "\u2699 \u81EA\u52A8\u4FEE\u590D\u547D\u540D" };
                fixBtn.AddToClassList("vs-action-btn-fix");
                actionBar.Add(fixBtn);
            }

            if (part.Status == PartStatus.Unrecognized)
            {
                var bindHint = new Label("\u2191 \u4ECE\u7C7B\u578B\u4E0B\u62C9\u9009\u62E9\u540E\u5373\u53EF\u7ED1\u5B9A");
                bindHint.AddToClassList("vs-bind-hint");
                actionBar.Add(bindHint);
            }

            if (part.Status != PartStatus.Ready && part.Status != PartStatus.Ignored)
            {
                var ignoreBtn = new Button(() =>
                {
                    _controller.IgnoreIssue(part);
                }) { text = "\u2014 \u5FFD\u7565\u6B64\u96F6\u4EF6" };
                ignoreBtn.AddToClassList("vs-action-btn-ignore");
                actionBar.Add(ignoreBtn);
            }

            // ── Ignored 恢复入口 ──
            if (part.Status == PartStatus.Ignored)
            {
                var restoreBtn = new Button(() =>
                {
                    _controller.RestoreFromIgnore(part);
                }) { text = "\u21B6 \u6062\u590D\u53C2\u4E0E" };
                restoreBtn.AddToClassList("vs-action-btn-restore");
                actionBar.Add(restoreBtn);
            }

            if (actionBar.childCount > 0)
                _bindingPanel.Add(actionBar);

            // ── 材质槽 ──
            RefreshMaterialSlots(part);
        }

        private void RefreshMaterialSlots(VehiclePart part)
        {
            if (_slotContainer == null) return;
            _slotContainer.Clear();

            if (part.MaterialSlots.Count == 0)
            {
                _slotContainer.Add(CreateHint("\u8BE5\u96F6\u4EF6\u6CA1\u6709 Renderer / \u6750\u8D28\u69FD"));
                return;
            }

            foreach (var slot in part.MaterialSlots)
            {
                var slotRow = new VisualElement();
                slotRow.AddToClassList("ws-slot-row");

                var indexLabel = new Label($"[{slot.Index}]");
                indexLabel.AddToClassList("ws-slot-index");

                var matLabel = new Label(slot.MaterialName);
                matLabel.AddToClassList("ws-slot-material");

                var shaderLabel = new Label(slot.ShaderName);
                shaderLabel.AddToClassList("ws-slot-shader");

                slotRow.Add(indexLabel);
                var col = new VisualElement();
                col.style.flexGrow = 1;
                col.Add(matLabel);
                col.Add(shaderLabel);
                slotRow.Add(col);

                _slotContainer.Add(slotRow);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 交互式 3D 预览 — 完全保留原有实现
        // ════════════════════════════════════════════════════════════════

        private void SetupInteractivePreview(GameObject prefab)
        {
            if (_previewArea == null) return;
            _previewArea.Clear();

            CleanupPreview();

            _previewUtility = new PreviewRenderUtility();

            var cam = _previewUtility.camera;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.11f, 0.13f, 1f);
            cam.cameraType = CameraType.Preview;

            _previewInstance = Object.Instantiate(prefab);
            _previewInstance.name = prefab.name;
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewUtility.AddSingleGO(_previewInstance);
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            SetLayerRecursive(_previewInstance.transform, _previewUtility.camera.gameObject.layer);

            _originalMaterials.Clear();
            _previewRendererMap.Clear();
            CacheRenderers(_previewInstance.transform, "");

            CreatePreviewMaterials();

            var bounds = CalculateBounds(_previewInstance);
            _orbitPivot = bounds.center;
            _orbitDistance = bounds.size.magnitude * 1.5f;
            _orbitYaw = 135f;
            _orbitPitch = 20f;

            SetupPreviewLighting();

            _imguiPreview = new IMGUIContainer(OnPreviewGUI);
            _imguiPreview.style.flexGrow = 1;

            // 节流：仅在元素可见时刷新预览，避免窗口隐藏时无意义渲染
            _imguiPreview.schedule.Execute(() =>
            {
                if (_imguiPreview.resolvedStyle.display != DisplayStyle.None
                    && _imguiPreview.panel != null
                    && IsElementVisible(_imguiPreview))
                {
                    _imguiPreview.MarkDirtyRepaint();
                }
            }).Every(33);

            _previewArea.Add(_imguiPreview);
        }

        private void CacheRenderers(Transform t, string parentPath)
        {
            var path = string.IsNullOrEmpty(parentPath) ? t.name : $"{parentPath}/{t.name}";
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                _originalMaterials[renderer] = renderer.sharedMaterials;
                _previewRendererMap[path] = renderer;
            }
            for (int i = 0; i < t.childCount; i++)
                CacheRenderers(t.GetChild(i), path);
        }

        private void CreatePreviewMaterials()
        {
            var highlightShader = Shader.Find("Standard");
            _highlightMaterial = new Material(highlightShader);
            _highlightMaterial.SetColor("_Color", new Color(0.2f, 0.6f, 1f, 1f));
            _highlightMaterial.SetColor("_EmissionColor", new Color(0.1f, 0.3f, 0.8f, 1f));
            _highlightMaterial.EnableKeyword("_EMISSION");
            _highlightMaterial.SetFloat("_Metallic", 0.3f);
            _highlightMaterial.SetFloat("_Glossiness", 0.8f);

            var dimShader = Shader.Find("Standard");
            _dimMaterial = new Material(dimShader);
            _dimMaterial.SetFloat("_Mode", 3f);
            _dimMaterial.SetColor("_Color", new Color(0.3f, 0.3f, 0.35f, 0.25f));
            _dimMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _dimMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _dimMaterial.SetInt("_ZWrite", 0);
            _dimMaterial.DisableKeyword("_ALPHATEST_ON");
            _dimMaterial.EnableKeyword("_ALPHABLEND_ON");
            _dimMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _dimMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void SetupPreviewLighting()
        {
            if (_previewUtility == null) return;

            var lights = _previewUtility.lights;
            if (lights != null && lights.Length > 0)
            {
                lights[0].type = LightType.Directional;
                lights[0].intensity = 1.4f;
                lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                lights[0].color = new Color(1f, 0.97f, 0.92f);
            }
            if (lights != null && lights.Length > 1)
            {
                lights[1].type = LightType.Directional;
                lights[1].intensity = 0.8f;
                lights[1].transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
                lights[1].color = new Color(0.7f, 0.8f, 1f);
            }
        }

        private void OnPreviewGUI()
        {
            if (_previewUtility == null || _imguiPreview == null) return;

            var rawRect = _imguiPreview.contentRect;
            if (float.IsNaN(rawRect.width) || float.IsNaN(rawRect.height)
                || rawRect.width < 2 || rawRect.height < 2)
                return;

            var rect = new Rect(0, 0, Mathf.Floor(rawRect.width), Mathf.Floor(rawRect.height));

            var evt = Event.current;
            HandlePreviewInput(evt, rect);
            UpdateOrbitCamera();

            try
            {
                _previewUtility.BeginPreview(rect, GUIStyle.none);
                _previewUtility.camera.Render();
                var resultRender = _previewUtility.EndPreview();
                GUI.DrawTexture(rect, resultRender, ScaleMode.StretchToFill, false);
            }
            catch (System.Exception)
            {
                // 窗口缩放/布局切换时可能出现瞬态异常，忽略即可
            }
        }

        private void HandlePreviewInput(Event evt, Rect rect)
        {
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 || evt.button == 2)
                    {
                        _isDragging = true;
                        _lastMousePos = evt.mousePosition;
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    _isDragging = false;
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        var delta = evt.mousePosition - _lastMousePos;
                        if (evt.button == 0)
                        {
                            _orbitYaw += delta.x * 0.5f;
                            _orbitPitch = Mathf.Clamp(_orbitPitch - delta.y * 0.5f, -80f, 80f);
                        }
                        else if (evt.button == 2)
                        {
                            var cam2 = _previewUtility.camera;
                            var right = cam2.transform.right;
                            var up = cam2.transform.up;
                            float panSpeed = _orbitDistance * 0.002f;
                            _orbitPivot -= right * delta.x * panSpeed + up * (-delta.y) * panSpeed;
                        }
                        _lastMousePos = evt.mousePosition;
                        evt.Use();
                        _imguiPreview?.MarkDirtyRepaint();
                    }
                    break;

                case EventType.ScrollWheel:
                    _orbitDistance *= 1f + evt.delta.y * 0.05f;
                    _orbitDistance = Mathf.Clamp(_orbitDistance, 0.5f, 100f);
                    evt.Use();
                    _imguiPreview?.MarkDirtyRepaint();
                    break;
            }
        }

        private void UpdateOrbitCamera()
        {
            if (_previewUtility == null) return;

            var cam = _previewUtility.camera;
            var rotation = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            var position = _orbitPivot + rotation * new Vector3(0f, 0f, -_orbitDistance);

            cam.transform.position = position;
            cam.transform.LookAt(_orbitPivot);
        }

        private void HighlightPartInPreview(VehiclePart selectedPart)
        {
            using var _t = PerfTrace.Begin("VehicleSetupView.HighlightPartInPreview");
            if (_previewInstance == null || _originalMaterials.Count == 0) return;

            // ── 取消选中 → 全部恢复原始材质 ──
            if (selectedPart == null)
            {
                _lastHighlightedRenderer = null;
                foreach (var kvp in _originalMaterials)
                {
                    if (kvp.Key != null)
                        kvp.Key.sharedMaterials = kvp.Value;
                }
                _imguiPreview?.MarkDirtyRepaint();
                return;
            }

            _previewRendererMap.TryGetValue(selectedPart.ObjectPath, out var selectedRenderer);

            foreach (var kvp in _originalMaterials)
            {
                if (kvp.Key == null) continue;

                if (kvp.Key == selectedRenderer)
                {
                    // 从缓存取或创建高亮材质（每个 Renderer 只创建一次）
                    if (!_highlightCache.TryGetValue(kvp.Key, out var highlighted))
                    {
                        var origMats = kvp.Value;
                        highlighted = new Material[origMats.Length];
                        for (int i = 0; i < origMats.Length; i++)
                        {
                            if (origMats[i] != null)
                            {
                                highlighted[i] = new Material(origMats[i]);
                                highlighted[i].hideFlags = HideFlags.HideAndDontSave;
                                highlighted[i].SetColor("_EmissionColor", new Color(0.05f, 0.15f, 0.4f));
                                highlighted[i].EnableKeyword("_EMISSION");
                            }
                            else
                            {
                                highlighted[i] = _highlightMaterial;
                            }
                        }
                        _highlightCache[kvp.Key] = highlighted;
                    }
                    kvp.Key.sharedMaterials = highlighted;
                }
                else
                {
                    // dim 材质共享单一实例，不需要缓存数组
                    var dimArray = new Material[kvp.Value.Length];
                    for (int i = 0; i < dimArray.Length; i++)
                        dimArray[i] = _dimMaterial;
                    kvp.Key.sharedMaterials = dimArray;
                }
            }

            _lastHighlightedRenderer = selectedRenderer;

            if (selectedRenderer != null)
                FocusCameraOnRenderer(selectedRenderer);

            _imguiPreview?.MarkDirtyRepaint();
        }

        private void FocusCameraOnRenderer(Renderer renderer)
        {
            var bounds = renderer.bounds;
            _orbitPivot = bounds.center;
            float size = bounds.size.magnitude;
            if (size < 0.01f) size = 1f;
            _orbitDistance = size * 3f;
            _orbitDistance = Mathf.Max(_orbitDistance, 1f);
        }

        private void ResetCameraToFullView()
        {
            if (_previewInstance == null) return;
            var bounds = CalculateBounds(_previewInstance);
            _orbitPivot = bounds.center;
            _orbitDistance = bounds.size.magnitude * 1.5f;
            _orbitYaw = 135f;
            _orbitPitch = 20f;
            _imguiPreview?.MarkDirtyRepaint();
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private void CleanupPreview()
        {
            _lastHighlightedRenderer = null;

            // 释放高亮缓存池中的所有临时材质
            foreach (var kvp in _highlightCache)
            {
                if (kvp.Value == null) continue;
                foreach (var mat in kvp.Value)
                {
                    if (mat != null && mat != _highlightMaterial)
                        Object.DestroyImmediate(mat);
                }
            }
            _highlightCache.Clear();

            _originalMaterials.Clear();
            _previewRendererMap.Clear();

            if (_previewInstance != null)
            {
                Object.DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_highlightMaterial != null)
            {
                Object.DestroyImmediate(_highlightMaterial);
                _highlightMaterial = null;
            }
            if (_dimMaterial != null)
            {
                Object.DestroyImmediate(_dimMaterial);
                _dimMaterial = null;
            }

            if (_previewUtility != null)
            {
                _previewUtility.Cleanup();
                _previewUtility = null;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Hierarchy 联动
        // ════════════════════════════════════════════════════════════════

        private void TrySelectInScene(VehiclePart part)
        {
            if (part == null || string.IsNullOrEmpty(part.ObjectPath)) return;

            var sceneObj = FindGameObjectByPath(part.ObjectPath);
            if (sceneObj != null)
            {
                Selection.activeGameObject = sceneObj;
                EditorGUIUtility.PingObject(sceneObj);
            }
            else
            {
                // 明确告知用户：对象未在当前上下文中找到
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                    _workspaceState.StatusMessage.Value =
                        $"\u5728 Prefab Stage \u4E2D\u672A\u627E\u5230\u300C{part.Name}\u300D\u2014\u8BF7\u786E\u8BA4\u5F53\u524D\u6253\u5F00\u7684 Prefab \u4E0E\u5BFC\u5165\u7684\u8F66\u8F86\u6A21\u578B\u4E00\u81F4";
                else
                    _workspaceState.StatusMessage.Value =
                        $"\u573A\u666F\u4E2D\u672A\u627E\u5230\u300C{part.Name}\u300D\u2014\u8BE5\u96F6\u4EF6\u53EF\u80FD\u5C1A\u672A\u5B9E\u4F8B\u5316\u5230\u573A\u666F\uFF0C\u8BF7\u5148\u5C06\u8F66\u8F86 Prefab \u62D6\u5165 Hierarchy";
            }
        }

        /// <summary>
        /// 按路径查找 GameObject。
        /// 优先在 Prefab Stage 中查找，回退到活动场景根对象。
        /// </summary>
        private static GameObject FindGameObjectByPath(string path)
        {
            using var _t = PerfTrace.Begin("VehicleSetupView.FindGameObjectByPath");
            if (string.IsNullOrEmpty(path)) return null;

            var segments = path.Split('/');
            if (segments.Length == 0) return null;

            // ── 优先：Prefab Stage ──
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                var prefabRoot = prefabStage.prefabContentsRoot;
                if (prefabRoot != null)
                {
                    var found = FindInHierarchy(prefabRoot, segments);
                    if (found != null) return found;
                }
            }

            // ── 回退：活动场景 ──
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root.name == segments[0])
                {
                    if (segments.Length == 1) return root;

                    var child = TraverseFromRoot(root, segments);
                    if (child != null) return child;
                }
            }

            return null;
        }

        /// <summary>在给定 Prefab 根下按路径段查找（根名称可能与 segments[0] 匹配或不匹配）。</summary>
        private static GameObject FindInHierarchy(GameObject prefabRoot, string[] segments)
        {
            // 情况 1：Prefab 根名称 == segments[0]（整个 Prefab 就是导入对象）
            if (prefabRoot.name == segments[0])
                return segments.Length == 1 ? prefabRoot : TraverseFromRoot(prefabRoot, segments);

            // 情况 2：在子树中查找 segments[0]
            var startTransform = prefabRoot.transform.Find(segments[0]);
            if (startTransform == null) return null;
            if (segments.Length == 1) return startTransform.gameObject;

            var current = startTransform.gameObject;
            for (int i = 1; i < segments.Length; i++)
            {
                var child = current.transform.Find(segments[i]);
                if (child == null) return null;
                current = child.gameObject;
            }
            return current;
        }

        private static GameObject TraverseFromRoot(GameObject root, string[] segments)
        {
            var current = root;
            for (int i = 1; i < segments.Length; i++)
            {
                var child = current.transform.Find(segments[i]);
                if (child == null) return null;
                current = child.gameObject;
            }
            return current;
        }

        // ════════════════════════════════════════════════════════════════
        // UI 辅助
        // ════════════════════════════════════════════════════════════════

        private static VisualElement CreatePanel(string className)
        {
            var panel = new VisualElement();
            panel.AddToClassList("ws-panel");
            panel.AddToClassList(className);
            return panel;
        }

        private static Label MakeTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ws-panel-title");
            return l;
        }

        private static Label MakeSectionTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ws-section-title");
            return l;
        }

        private static Label CreateHint(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ws-hint");
            return l;
        }

        private static VisualElement CreateFormRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ws-form-row");
            row.Add(CreateFormLabel(label));
            var val = new Label(value ?? "");
            val.AddToClassList("ws-form-value");
            row.Add(val);
            return row;
        }

        private static Label CreateFormLabel(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ws-form-label");
            return l;
        }

        /// <summary>
        /// 检查元素是否实际可见（沿父链检查 display 和 visibility）。
        /// 用于节流：窗口最小化/标签页切换时跳过渲染。
        /// </summary>
        private static bool IsElementVisible(VisualElement element)
        {
            var current = element;
            while (current != null)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                    return false;
                if (current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
                current = current.parent;
            }
            return true;
        }

        private static string StatusIcon(PartStatus status)
        {
            return status switch
            {
                PartStatus.Ready        => "\u2713",
                PartStatus.NeedsFix     => "\u26A0",
                PartStatus.Unrecognized => "?",
                PartStatus.Ignored      => "\u2014",
                _                       => "",
            };
        }

        // ── 进度指示器 ──

        private static Label CreateStepIndicator(string number, bool active)
        {
            var label = new Label(number);
            label.AddToClassList("ws-step-indicator");
            if (active) label.AddToClassList("ws-step-active");
            return label;
        }

        private static Label CreateStepLabel(string text, bool active)
        {
            var label = new Label(text);
            label.AddToClassList("ws-step-label");
            if (active) label.AddToClassList("ws-step-label-active");
            return label;
        }

        private static VisualElement CreateStepNode(Label indicator, Label label)
        {
            var node = new VisualElement();
            node.AddToClassList("ws-step-node");
            node.Add(indicator);
            node.Add(label);
            return node;
        }

        private static VisualElement CreateStepConnector()
        {
            var connector = new VisualElement();
            connector.AddToClassList("ws-step-connector");
            return connector;
        }

        private void SetProgressStep(int step)
        {
            if (step > 1)
            {
                _step1Indicator?.RemoveFromClassList("ws-step-active");
                _step1Indicator?.AddToClassList("ws-step-done");
                if (_step1Indicator != null) _step1Indicator.text = "\u2713";
                _step1Label?.RemoveFromClassList("ws-step-label-active");
                _step1Label?.AddToClassList("ws-step-label-done");
            }

            if (step >= 2)
            {
                _step2Indicator?.AddToClassList("ws-step-active");
                _step2Label?.AddToClassList("ws-step-label-active");
            }
            if (step > 2)
            {
                _step2Indicator?.RemoveFromClassList("ws-step-active");
                _step2Indicator?.AddToClassList("ws-step-done");
                if (_step2Indicator != null) _step2Indicator.text = "\u2713";
                _step2Label?.RemoveFromClassList("ws-step-label-active");
                _step2Label?.AddToClassList("ws-step-label-done");
            }

            if (step >= 3)
            {
                _step3Indicator?.AddToClassList("ws-step-active");
                _step3Label?.AddToClassList("ws-step-label-active");
            }
        }
    }
}
