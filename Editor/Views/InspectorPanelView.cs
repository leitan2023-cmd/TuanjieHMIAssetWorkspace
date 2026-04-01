using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// RightPanel 视图（implements IInspectorView）。
    /// 分三个区块：Asset Info / Target Context / Actions。
    /// Controller 可通过 IInspectorView 接口调用 ShowConfirmDialog / FlashActionButton。
    /// </summary>
    public sealed class InspectorPanelView : IInspectorView
    {
        private readonly VisualElement _root;

        private Button _applyBtn;
        private Label _applyReasonLabel;
        private Label _targetLabel;
        private ActionController _actionController;

        public InspectorPanelView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, ActionController actionController)
        {
            if (_root == null) return;
            _actionController = actionController;

            // 清空 UXML 原有子元素，完全用代码构建结构化布局
            _root.Clear();

            // ── 区块 1: Asset Info ─────────────────────────────────
            _root.Add(CreateSectionTitle("Asset Info"));

            var assetNameLabel = new Label("No Selection");
            assetNameLabel.AddToClassList("inspector-value-primary");
            _root.Add(assetNameLabel);

            var assetTypeLabel = new Label();
            assetTypeLabel.AddToClassList("inspector-value-secondary");
            _root.Add(assetTypeLabel);

            var assetPathLabel = new Label();
            assetPathLabel.AddToClassList("inspector-value-secondary");
            _root.Add(assetPathLabel);

            // 预览图
            var preview = new Image { name = "asset-preview" };
            preview.AddToClassList("asset-preview");
            _root.Add(preview);

            // 绑定资产信息
            state.SelectedAsset.BindToLabel(assetNameLabel, a => a?.DisplayName ?? "No Selection");
            state.SelectedAsset.Changed += (_, asset) =>
            {
                assetTypeLabel.text = asset != null ? $"Type:  {asset.Kind}" : "";
                assetPathLabel.text = asset != null ? $"Path:  {asset.Path}" : "";
            };
            PreviewEvents.TextureReady.Subscribe(evt => preview.image = evt.Texture);

            // ── 区块 2: Target Context ────────────────────────────
            _root.Add(CreateSeparator());
            _root.Add(CreateSectionTitle("Target Context"));

            _targetLabel = new Label("No Unity Selection");
            _targetLabel.AddToClassList("inspector-value-secondary");
            _root.Add(_targetLabel);

            // 当 Unity Hierarchy 选中对象变化时，刷新 Target Context
            state.UnitySelection.Changed += (_, obj) => RefreshTargetContext(obj);

            // ── 区块 3: Actions ───────────────────────────────────
            _root.Add(CreateSeparator());
            _root.Add(CreateSectionTitle("Actions"));

            _applyBtn = new Button { text = "Apply to Selection" };
            _applyBtn.AddToClassList("action-btn");
            _applyBtn.RegisterCallback<ClickEvent>(_ => actionController.ApplyToSelection());
            _root.Add(_applyBtn);

            _applyReasonLabel = new Label();
            _applyReasonLabel.AddToClassList("inspector-value-secondary");
            _applyReasonLabel.style.whiteSpace = WhiteSpace.Normal;
            _applyReasonLabel.style.marginTop = 4;
            _root.Add(_applyReasonLabel);

            // 订阅状态变化，刷新按钮
            ActionEvents.StatesChanged.Subscribe(_ => RefreshApplyButton());
            RefreshApplyButton();
        }

        // ── 刷新逻辑 ──────────────────────────────────────────────

        /// <summary>
        /// 显示当前 Hierarchy 选中对象名 + 是否有 Renderer
        /// </summary>
        private void RefreshTargetContext(Object obj)
        {
            if (_targetLabel == null) return;
            if (obj == null)
            {
                _targetLabel.text = "No Unity Selection";
                return;
            }
            // 如果是 GameObject，检查 Renderer
            if (obj is GameObject go)
            {
                var renderer = go.GetComponent<Renderer>();
                _targetLabel.text = renderer != null
                    ? $"{go.name}  ({renderer.GetType().Name})"
                    : $"{go.name}  (no Renderer)";
            }
            else
            {
                _targetLabel.text = $"{obj.name}  ({obj.GetType().Name})";
            }
        }

        private void RefreshApplyButton()
        {
            if (_applyBtn == null || _actionController == null) return;
            var (canApply, reason) = _actionController.QueryApplyState();
            _applyBtn.SetEnabled(canApply);
            _applyBtn.tooltip = reason;
            if (_applyReasonLabel != null)
                _applyReasonLabel.text = reason;
        }

        // ── UI 构建辅助 ────────────────────────────────────────────

        private static Label CreateSectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("section-title");
            return label;
        }

        private static VisualElement CreateSeparator()
        {
            var sep = new VisualElement();
            sep.AddToClassList("inspector-separator");
            return sep;
        }

        // ── IInspectorView 接口实现 ─────────────────────────────────

        /// <summary>
        /// 弹出确认对话框（用于 Batch Replace 等高风险操作）
        /// </summary>
        public void ShowConfirmDialog(string title, string message, Action onConfirm)
        {
            if (EditorUtility.DisplayDialog(title, message, "Confirm", "Cancel"))
                onConfirm?.Invoke();
        }

        /// <summary>
        /// 短暂高亮某个操作按钮（视觉反馈）
        /// </summary>
        public void FlashActionButton(string actionName)
        {
            // Phase 2: 按钮短暂变色动画
        }
    }
}
