using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// TopBar 视图（implements ITopBarView）。
    /// 显示 Unity Selection / Scene / Pipeline 信息，搜索栏和命令栏。
    /// Controller 可通过 ITopBarView 接口控制自动补全和命令栏文本。
    /// </summary>
    public sealed class TopBarView : ITopBarView
    {
        private readonly VisualElement _root;
        private TextField _commandField;

        public TopBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, AIController aiController, AssetBrowserController assetBrowserController)
        {
            if (_root == null) return;
            var selectionLabel = _root.Q<Label>("selection-label");
            var sceneLabel = _root.Q<Label>("scene-label");
            var pipelineLabel = _root.Q<Label>("pipeline-label");
            _commandField = _root.Q<TextField>("command-input");
            var searchField = _root.Q<ToolbarSearchField>("search-input");

            // selection-label：显示 Unity Editor 当前选中对象名称
            state.UnitySelection.BindToLabel(selectionLabel,
                obj => obj != null ? obj.name : "None");

            state.ActiveScene.BindToLabel(sceneLabel,
                s => string.IsNullOrEmpty(s.Name) ? "Untitled Scene" : s.Name);

            state.PipelineName.BindToLabel(pipelineLabel);

            _commandField?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return)
                    aiController.ExecuteCommand(_commandField.value);
            });

            searchField?.RegisterValueChangedCallback(evt => assetBrowserController.ApplySearch(evt.newValue));
        }

        // ── ITopBarView 接口实现 ────────────────────────────────────

        /// <summary>
        /// 显示命令自动补全（Phase 2 AI 集成时实现）
        /// </summary>
        public void ShowAutocompleteDropdown(List<string> suggestions)
        {
            // Phase 2: 弹出自动补全浮动面板
        }

        /// <summary>
        /// 隐藏命令自动补全
        /// </summary>
        public void HideAutocompleteDropdown()
        {
            // Phase 2
        }

        /// <summary>
        /// 设置命令栏文本（AI fill command）
        /// </summary>
        public void SetCommandText(string text)
        {
            if (_commandField != null)
                _commandField.value = text;
        }
    }
}
