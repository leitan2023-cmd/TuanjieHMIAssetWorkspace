using UnityEditor.UIElements;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    public sealed class TopBarView
    {
        private readonly VisualElement _root;

        public TopBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state, AIController aiController, AssetBrowserController assetBrowserController)
        {
            if (_root == null) return;
            var selectionLabel = _root.Q<Label>("selection-label");
            var sceneLabel = _root.Q<Label>("scene-label");
            var pipelineLabel = _root.Q<Label>("pipeline-label");
            var commandField = _root.Q<TextField>("command-input");
            var searchField = _root.Q<ToolbarSearchField>("search-input");

            // selection-label：显示 Unity Editor 当前选中对象名称（非 Workspace 内选择）
            // 接入真实数据后，在 Unity Hierarchy/Project 窗口点击任意对象即可看到变化
            state.UnitySelection.BindToLabel(selectionLabel,
                obj => obj != null ? obj.name : "None");

            state.ActiveScene.BindToLabel(sceneLabel,
                s => string.IsNullOrEmpty(s.Name) ? "Untitled Scene" : s.Name);

            state.PipelineName.BindToLabel(pipelineLabel);

            commandField?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Return)
                    aiController.ExecuteCommand(commandField.value);
            });

            searchField?.RegisterValueChangedCallback(evt => assetBrowserController.ApplySearch(evt.newValue));
        }
    }
}
