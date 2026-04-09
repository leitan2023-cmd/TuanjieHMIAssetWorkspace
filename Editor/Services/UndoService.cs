using UnityEditor;
using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    public sealed class UndoService : IUndoService
    {
        public void RecordObject(Object target, string name)
        {
            if (target != null) Undo.RecordObject(target, name);
        }

        public void RegisterCreatedObject(Object obj, string name)
        {
            if (obj != null) Undo.RegisterCreatedObjectUndo(obj, name);
        }

        public void SetGroupName(string name)
        {
            Undo.SetCurrentGroupName(name);
        }

        public void PerformUndo()
        {
            Undo.PerformUndo();
        }
    }
}
