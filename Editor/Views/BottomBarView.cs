using UnityEngine.UIElements;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    public sealed class BottomBarView
    {
        private readonly VisualElement _root;

        public BottomBarView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state)
        {
            if (_root == null) return;
            var status = _root.Q<Label>("status-message");
            state.StatusMessage.BindToLabel(status);
            ActionEvents.Executed.Subscribe(evt => status.text = evt.Message);
            ActionEvents.Failed.Subscribe(evt => status.text = evt.Reason);
        }
    }
}
