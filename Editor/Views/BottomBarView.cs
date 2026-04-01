using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 底部状态栏（implements IBottomBarView）。
    /// 显示最近操作结果，成功/失败带颜色区分。
    /// Controller 可通过 IBottomBarView.ShowTransientMessage 发送临时消息。
    /// </summary>
    public sealed class BottomBarView : IBottomBarView
    {
        // 成功 = 偏绿，失败 = 偏橙，普通 = 默认灰白
        private static readonly Color ColorSuccess = new Color(0.4f, 0.85f, 0.4f);
        private static readonly Color ColorFailed  = new Color(0.95f, 0.6f, 0.3f);
        private static readonly Color ColorNormal  = new Color(0.6f, 0.6f, 0.65f);

        private readonly VisualElement _root;
        private Label _statusLabel;
        private double _transientExpireTime;

        public BottomBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state)
        {
            if (_root == null) return;
            _statusLabel = _root.Q<Label>("status-message");

            // 普通状态消息（灰色）
            state.StatusMessage.BindToLabel(_statusLabel);
            state.StatusMessage.Changed += (_, _) =>
                _statusLabel.style.color = new StyleColor(ColorNormal);

            // 操作成功（绿色）
            ActionEvents.Executed.Subscribe(evt =>
            {
                _statusLabel.text = $"\u2713  {evt.Message}";
                _statusLabel.style.color = new StyleColor(ColorSuccess);
            });

            // 操作失败（橙色）
            ActionEvents.Failed.Subscribe(evt =>
            {
                _statusLabel.text = $"\u2717  {evt.Reason}";
                _statusLabel.style.color = new StyleColor(ColorFailed);
            });
        }

        // ── IBottomBarView 接口实现 ─────────────────────────────────

        /// <summary>
        /// 显示一条临时消息，durationSeconds 秒后自动恢复为 "Ready"
        /// </summary>
        public void ShowTransientMessage(string message, float durationSeconds)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.style.color = new StyleColor(ColorNormal);
            _transientExpireTime = EditorApplication.timeSinceStartup + durationSeconds;
            EditorApplication.update += CheckTransientExpiry;
        }

        private void CheckTransientExpiry()
        {
            if (EditorApplication.timeSinceStartup < _transientExpireTime) return;
            EditorApplication.update -= CheckTransientExpiry;
            if (_statusLabel != null)
            {
                _statusLabel.text = "Ready";
                _statusLabel.style.color = new StyleColor(ColorNormal);
            }
        }
    }
}
