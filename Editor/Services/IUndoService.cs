using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    public interface IUndoService
    {
        void RecordObject(Object target, string name);
        void RegisterCreatedObject(Object obj, string name);
    }
}
