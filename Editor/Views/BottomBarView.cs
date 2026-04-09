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
    /// 底部状态栏（implements IBottomBarView）。
    /// 产品级设计：操作反馈 + 撤销按钮 + 操作计数。
    ///
    /// 消费来源：
    ///   WorkspaceState.StatusMessage → 常规状态
    ///   ActionEvents.Executed        → 成功反馈（绿色）
    ///   ActionEvents.Failed          → 失败反馈（橙色）
    ///   CommandHistory               → 操作计数
    /// </summary>
    public sealed class BottomBarView : IBottomBarView
    {
        private static readonly Color ColorSuccess = new(0.35f, 0.82f, 0.42f);
        private static readonly Color ColorFailed  = new(0.92f, 0.52f, 0.28f);
        private static readonly Color ColorNormal  = new(0.52f, 0.53f, 0.58f);

        private readonly VisualElement _root;
        private Label _statusLabel;
        private Label _statusDot;
        private Button _undoBtn;
        private Label _counterLabel;
        private double _transientExpireTime;
        private ActionController _actionController;

        public BottomBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, ActionController actionController = null,
            CommandHistory commandHistory = null)
        {
            if (_root == null) return;
            _actionController = actionController;

            _statusLabel = _root.Q<Label>("status-message");
            _statusDot = _root.Q<Label>("status-icon");
            _undoBtn = _root.Q<Button>("undo-btn");

            // ── 操作计数标签（新增，插入到 undo 按钮左侧） ──
            _counterLabel = _root.Q<Label>("action-counter");
            if (_counterLabel == null)
            {
                _counterLabel = new Label("");
                _counterLabel.AddToClassList("bb-action-counter");
                _counterLabel.name = "action-counter";
                // 尝试在 undo 按钮前插入
                if (_undoBtn != null && _undoBtn.parent != null)
                {
                    var idx = _undoBtn.parent.IndexOf(_undoBtn);
                    _undoBtn.parent.Insert(idx, _counterLabel);
                }
                else
                {
                    _root.Add(_counterLabel);
                }
            }

            // ── 常规状态消息 ──
            state.StatusMessage.BindToLabel(_statusLabel);
            state.StatusMessage.Changed += (_, _) =>
            {
                SetDotState("success");
                if (_statusLabel != null) _statusLabel.style.color = new StyleColor(ColorNormal);
            };

            // ── 操作成功（绿色反馈 + 短暂高亮） ──
            ActionEvents.Executed.Subscribe(evt =>
            {
                if (_statusLabel != null)
                {
                    _statusLabel.text = $"\u2713  {evt.Message}";
                    _statusLabel.style.color = new StyleColor(ColorSuccess);
                }
                SetDotState("success");

                // 成功后 5 秒恢复正常色
                ScheduleColorReset(5f);

                // 更新操作计数
                RefreshCounter(commandHistory);
            });

            // ── 操作失败（橙色反馈 + 持续显示） ──
            ActionEvents.Failed.Subscribe(evt =>
            {
                if (_statusLabel != null)
                {
                    _statusLabel.text = $"\u2717  {evt.Reason}";
                    _statusLabel.style.color = new StyleColor(ColorFailed);
                }
                SetDotState("warning");

                // 失败持续显示 8 秒
                ScheduleColorReset(8f);

                RefreshCounter(commandHistory);
            });

            // ── 撤销按钮 ──
            _undoBtn?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_actionController != null)
                    _actionController.PerformUndo();
                else
                    Undo.PerformUndo();

                if (_statusLabel != null)
                {
                    _statusLabel.text = "\u21A9  \u5DF2\u64A4\u9500\u4E0A\u6B21\u64CD\u4F5C";
                    _statusLabel.style.color = new StyleColor(ColorNormal);
                }
                SetDotState("success");
            });

            // 初始计数
            RefreshCounter(commandHistory);
        }

        // ── 兼容旧调用签名 ──
        public void Bind(WorkspaceState state)
        {
            Bind(state, null, null);
        }

        // ── IBottomBarView 接口实现 ──

        public void ShowTransientMessage(string message, float durationSeconds)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.style.color = new StyleColor(ColorNormal);
            _transientExpireTime = EditorApplication.timeSinceStartup + durationSeconds;
            EditorApplication.update += CheckTransientExpiry;
        }

        // ── 内部 ──

        private void RefreshCounter(CommandHistory history)
        {
            if (_counterLabel == null || history == null) return;
            var total = history.Records.Count;
            if (total > 0)
            {
                var lastRecord = history.Records[total - 1];
                var successCount = 0;
                var failCount = 0;
                foreach (var r in history.Records)
                {
                    if (r.Success) successCount++;
                    else failCount++;
                }
                _counterLabel.text = failCount > 0
                    ? $"{successCount}\u2713 {failCount}\u2717"
                    : $"{successCount} \u64CD\u4F5C";
                _counterLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _counterLabel.style.display = DisplayStyle.None;
            }
        }

        private void ScheduleColorReset(float seconds)
        {
            var resetTime = EditorApplication.timeSinceStartup + seconds;

            void ResetColor()
            {
                if (EditorApplication.timeSinceStartup < resetTime) return;
                EditorApplication.update -= ResetColor;
                if (_statusLabel != null)
                    _statusLabel.style.color = new StyleColor(ColorNormal);
                SetDotState("success");
            }

            EditorApplication.update += ResetColor;
        }

        private void CheckTransientExpiry()
        {
            if (EditorApplication.timeSinceStartup < _transientExpireTime) return;
            EditorApplication.update -= CheckTransientExpiry;
            if (_statusLabel != null)
            {
                _statusLabel.text = "\u5C31\u7EEA";
                _statusLabel.style.color = new StyleColor(ColorNormal);
            }
            SetDotState("success");
        }

        private void SetDotState(string state)
        {
            if (_statusDot == null) return;
            _statusDot.RemoveFromClassList("success");
            _statusDot.RemoveFromClassList("warning");
            _statusDot.RemoveFromClassList("error");
            _statusDot.AddToClassList(state);
        }
    }
}
