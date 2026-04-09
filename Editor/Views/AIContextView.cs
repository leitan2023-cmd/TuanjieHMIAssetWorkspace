using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// AI 面板 — 产品级设计：建议卡片 + 分析 + 操作按钮。
    /// 不再是聊天日志，而是上下文感知的智能助手界面。
    /// </summary>
    public sealed class AIContextView
    {
        private readonly VisualElement _root;
        private readonly List<(string role, string text)> _messages = new();
        private ScrollView _chatScroll;
        private VisualElement _suggestionsContainer;
        private VisualElement _analysisContainer;
        private TextField _input;
        private WorkspaceState _state;
        private AIController _aiController;

        public AIContextView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, AIController aiController)
        {
            if (_root == null) return;

            _state = state;
            _aiController = aiController;

            _root.Clear();
            _root.AddToClassList("ai-panel");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _root.Add(scroll);

            // ── 建议区块 ──
            _suggestionsContainer = new VisualElement();
            scroll.Add(_suggestionsContainer);
            BuildDefaultSuggestions();

            // ── 分析区块 ──
            _analysisContainer = new VisualElement();
            scroll.Add(_analysisContainer);

            // ── 快捷操作 ──
            var quickRow = new VisualElement();
            quickRow.AddToClassList("ai-quick-row");
            quickRow.style.marginTop = 12;
            quickRow.Add(CreateQuickButton("\u2728 推荐变体"));
            quickRow.Add(CreateQuickButton("\u2602 查找关联"));
            quickRow.Add(CreateQuickButton("\u25C6 分析材质"));
            quickRow.Add(CreateQuickButton("\u21BB 准备替换"));
            scroll.Add(quickRow);

            // ── 聊天历史（折叠在下方）──
            _chatScroll = new ScrollView(ScrollViewMode.Vertical);
            _chatScroll.AddToClassList("ai-chat-scroll");
            _chatScroll.style.maxHeight = 200;
            scroll.Add(_chatScroll);

            // ── 输入行 ──
            var inputRow = new VisualElement();
            inputRow.AddToClassList("ai-input-row");

            _input = new TextField { value = string.Empty };
            _input.AddToClassList("ai-input");
            _input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return)
                    SubmitPrompt(_input.value);
            });

            var sendButton = new Button(() => SubmitPrompt(_input.value)) { text = "发送" };
            sendButton.AddToClassList("ai-send-btn");

            inputRow.Add(_input);
            inputRow.Add(sendButton);
            _root.Add(inputRow);

            // ── 响应式 ──
            state.SelectedAsset.Changed += (_, asset) =>
            {
                if (asset != null)
                {
                    BuildAssetAnalysis(asset);
                    BuildAssetSuggestions(asset);
                }
                else
                {
                    BuildDefaultSuggestions();
                    _analysisContainer?.Clear();
                }
            };

            AIEvents.CommandResult.Subscribe(OnCommandResult);
        }

        // ════════════════════════════════════════════════════════════════
        // 建议卡片
        // ════════════════════════════════════════════════════════════════

        private void BuildDefaultSuggestions()
        {
            if (_suggestionsContainer == null) return;
            _suggestionsContainer.Clear();

            AddSuggestionCard(
                "\u2606", "开始探索",
                "从左侧选择一个资产或在搜索栏中输入关键字，AI 将自动分析并提供推荐。",
                null);

            AddSuggestionCard(
                "\u25B6", "导入车辆",
                "使用车辆设置工作区导入 FBX 文件并自动识别零件类型。",
                "进入车辆设置", () =>
                {
                    if (_state != null)
                        _state.CurrentViewMode.Value = ViewMode.VehicleSetup;
                });

            AddSuggestionCard(
                "\u21BB", "批量替换材质",
                "选择目标材质，浏览候选并一键替换场景中所有匹配对象。",
                "进入批量替换", () =>
                {
                    if (_state != null)
                        _state.CurrentViewMode.Value = ViewMode.BatchReplace;
                });
        }

        private void BuildAssetSuggestions(AssetEntry asset)
        {
            if (_suggestionsContainer == null) return;
            _suggestionsContainer.Clear();

            if (asset.Kind == AssetKind.Material)
            {
                AddSuggestionCard(
                    "\u2728", $"应用 {asset.DisplayName}",
                    "将此材质应用到 Hierarchy 中当前选中的 GameObject。确保目标对象有 Renderer 组件。",
                    "应用到选中对象", () =>
                    {
                        SubmitPrompt($"apply {asset.DisplayName}");
                    });

                AddSuggestionCard(
                    "\u2602", "查找相似材质",
                    $"基于 {asset.DisplayName} 的 Shader 和属性，在工作区中查找相似的材质变体。",
                    "查找相似", () =>
                    {
                        SubmitPrompt($"查找关联: {asset.DisplayName}");
                    });
            }
            else if (asset.Kind == AssetKind.Prefab || asset.Kind == AssetKind.Model)
            {
                AddSuggestionCard(
                    "\u25A0", $"检查 {asset.DisplayName}",
                    "分析此预制体的材质引用和子对象结构，检查命名规范。",
                    "开始分析", () =>
                    {
                        SubmitPrompt($"解释选中项: {asset.DisplayName}");
                    });
            }
            else
            {
                AddSuggestionCard(
                    "\u25CB", $"查看 {asset.DisplayName}",
                    $"类型：{asset.Kind}  路径：{asset.Path}",
                    null);
            }
        }

        private void AddSuggestionCard(string icon, string title, string desc, string actionText, System.Action onClick = null)
        {
            var card = new VisualElement();
            card.AddToClassList("ai-suggestion-card");

            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("ai-suggestion-icon");
            card.Add(iconLabel);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ai-suggestion-title");
            card.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("ai-suggestion-desc");
            card.Add(descLabel);

            if (!string.IsNullOrEmpty(actionText) && onClick != null)
            {
                var btn = new Button(onClick) { text = actionText };
                btn.AddToClassList("ai-action-btn");
                card.Add(btn);
            }

            _suggestionsContainer.Add(card);
        }

        // ════════════════════════════════════════════════════════════════
        // 分析面板
        // ════════════════════════════════════════════════════════════════

        private void BuildAssetAnalysis(AssetEntry asset)
        {
            if (_analysisContainer == null) return;
            _analysisContainer.Clear();

            var titleLabel = new Label("资产分析");
            titleLabel.AddToClassList("ws-section-title");
            _analysisContainer.Add(titleLabel);

            AddAnalysisRow("名称", asset.DisplayName);
            AddAnalysisRow("类型", asset.Kind.ToString());
            AddAnalysisRow("分类", asset.Category ?? "未分类");
            if (!string.IsNullOrEmpty(asset.FileSize))
                AddAnalysisRow("大小", asset.FileSize);
            if (asset.Tags != null && asset.Tags.Length > 0)
                AddAnalysisRow("标签", string.Join(", ", asset.Tags));
        }

        private void AddAnalysisRow(string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ai-analysis-row");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("ai-analysis-key");

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("ai-analysis-value");

            row.Add(keyLabel);
            row.Add(valueLabel);
            _analysisContainer.Add(row);
        }

        // ════════════════════════════════════════════════════════════════
        // 聊天
        // ════════════════════════════════════════════════════════════════

        private Button CreateQuickButton(string text)
        {
            return new Button(() =>
            {
                var assetName = _state?.SelectedAsset.Value?.DisplayName ?? "当前选中项";
                SubmitPrompt($"{text}: {assetName}");
            })
            {
                text = text
            }.WithClass("ai-quick-btn");
        }

        private void SubmitPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) || _aiController == null) return;
            AddMessage("user", prompt.Trim());
            RenderMessages();
            _aiController.ExecuteCommand(prompt.Trim());
            if (_input != null) _input.value = string.Empty;
        }

        private void OnCommandResult(CommandResultReadyEvent evt)
        {
            AddMessage("ai", evt.Result);
            RenderMessages();
        }

        private void AddMessage(string role, string text)
        {
            _messages.Add((role, text));
        }

        private void RenderMessages()
        {
            if (_chatScroll == null) return;
            _chatScroll.Clear();

            // 只显示最近 6 条
            var start = Mathf.Max(0, _messages.Count - 6);
            for (var i = start; i < _messages.Count; i++)
            {
                var message = _messages[i];
                var bubble = new VisualElement();
                bubble.AddToClassList("ai-message");
                if (message.role == "user")
                    bubble.AddToClassList("user");

                var role = new Label(message.role == "user" ? "你" : "AI");
                role.AddToClassList("ai-message-role");

                var text = new Label(message.text);
                text.AddToClassList("ai-message-text");

                bubble.Add(role);
                bubble.Add(text);
                _chatScroll.Add(bubble);
            }

            _chatScroll.scrollOffset = new Vector2(0f, float.MaxValue);
        }
    }

    internal static class ButtonExtensions
    {
        public static Button WithClass(this Button button, string className)
        {
            button.AddToClassList(className);
            return button;
        }
    }
}
