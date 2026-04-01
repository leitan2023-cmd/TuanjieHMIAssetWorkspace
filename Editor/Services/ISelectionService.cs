using System;
using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    /// <summary>
    /// 选择服务接口：封装 Unity Editor 的 Selection API
    /// </summary>
    public interface ISelectionService
    {
        UnityEngine.Object GetActiveObject();
        GameObject GetActiveGameObject();
        void SetActiveObject(UnityEngine.Object obj);
        void PingObject(UnityEngine.Object obj);
        event Action SelectionChanged;
    }
}
