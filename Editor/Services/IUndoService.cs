using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    public interface IUndoService
    {
        void RecordObject(Object target, string name);
        void RegisterCreatedObject(Object obj, string name);

        /// <summary>为当前 Undo 组设置名称（用于批量操作合并为单次撤销）。</summary>
        void SetGroupName(string name);

        /// <summary>执行一次撤销操作。</summary>
        void PerformUndo();
    }
}
