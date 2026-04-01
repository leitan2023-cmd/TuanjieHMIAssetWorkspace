using System.Collections.Generic;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class WorkspaceController : IController
    {
        private readonly WorkspaceState _state;
        private readonly List<IController> _children = new();

        public WorkspaceController(WorkspaceState state)
        {
            _state = state;
        }

        public void AddChild(IController controller)
        {
            if (controller != null) _children.Add(controller);
        }

        public void Initialize()
        {
            foreach (var child in _children) child.Initialize();
            _state.StatusMessage.Value = "Workspace ready";
        }

        public void Dispose()
        {
            for (var i = _children.Count - 1; i >= 0; i--) _children[i].Dispose();
            _children.Clear();
        }
    }
}
