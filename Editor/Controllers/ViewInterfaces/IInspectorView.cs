using System;

namespace HMI.Workspace.Editor.Controllers.ViewInterfaces
{
    public interface IInspectorView
    {
        void ShowConfirmDialog(string title, string message, Action onConfirm);
        void FlashActionButton(string actionName);
    }
}
