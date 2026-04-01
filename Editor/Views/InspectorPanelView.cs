using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// RightPanel 视图：显示选中资产名称、预览图、Apply 按钮。
    /// 按钮状态由 ActionController.QueryApplyState() 驱动，
    /// 不满足条件时禁用并显示原因文本。
    /// </summary>
    public sealed class InspectorPanelView
    {
        private readonly VisualElement _root;

        // 缓存按钮和原因标签的引用，供状态刷新时使用
        private Button _applyBtn;
        private Label _applyReasonLabel;
        private ActionController _actionController;

        public InspectorPanelView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, ActionController actionController)
        {
            if (_root == null) return;
            _actionController = actionController;

            var title = _root.Q<Label>("inspector-asset-name");
            var preview = _root.Q<Image>("asset-preview");
            _applyBtn = _root.Q<Button>("apply-btn");
            var instantiateBtn = _root.Q<Button>("instantiate-btn");

            // 选中资产名称绑定
            state.SelectedAsset.BindToLabel(title, a => a?.DisplayName ?? "No Selection");

            // 预览图绑定
            PreviewEvents.TextureReady.Subscribe(evt => preview.image = evt.Texture);

            // Apply 按钮点击
            _applyBtn?.RegisterCallback<ClickEvent>(_ => actionController.ApplyToSelection());

            // 在 Apply 按钮下方添加原因提示标签
            if (_applyBtn != null)
            {
                _applyReasonLabel = new Label { name = "apply-reason" };
                _applyReasonLabel.style.fontSize = 11;
                _applyReasonLabel.style.color = new StyleColor(new UnityEngine.Color(0.6f, 0.6f, 0.6f));
                _applyReasonLabel.style.whiteSpace = WhiteSpace.Normal;
                _applyReasonLabel.style.marginTop = 4;
                // 插入到 apply-btn 之后
                var btnIndex = _root.IndexOf(_applyBtn);
                if (btnIndex >= 0 && btnIndex < _root.childCount - 1)
                    _root.Insert(btnIndex + 1, _applyReasonLabel);
                else
                    _root.Add(_applyReasonLabel);
            }

            // 隐藏 Instantiate 按钮（本阶段不实现）
            if (instantiateBtn != null)
                instantiateBtn.style.display = DisplayStyle.None;

            // 订阅 ActionStatesChanged 事件，刷新按钮可用状态
            ActionEvents.StatesChanged.Subscribe(_ => RefreshApplyButton());

            // 初始刷新一次
            RefreshApplyButton();
        }

        /// <summary>
        /// 根据 ActionController.QueryApplyState() 的返回值
        /// 设置 Apply 按钮的 enabled 状态、tooltip 和原因文本。
        /// </summary>
        private void RefreshApplyButton()
        {
            if (_applyBtn == null || _actionController == null) return;

            var (canApply, reason) = _actionController.QueryApplyState();

            _applyBtn.SetEnabled(canApply);
            _applyBtn.tooltip = reason;

            if (_applyReasonLabel != null)
                _applyReasonLabel.text = reason;
        }
    }
}
