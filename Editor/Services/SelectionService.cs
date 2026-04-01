using System;
using UnityEditor;
using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    /// <summary>
    /// 选择服务：封装 Unity Selection API，避免 View/Controller 直接调用
    /// </summary>
    public sealed class SelectionService : ISelectionService
    {
        public event Action SelectionChanged;

        public SelectionService()
        {
            Selection.selectionChanged += RaiseSelectionChanged;
        }

        public UnityEngine.Object GetActiveObject() => Selection.activeObject;
        public GameObject GetActiveGameObject() => Selection.activeGameObject;
        public void SetActiveObject(UnityEngine.Object obj) => Selection.activeObject = obj;
        public void PingObject(UnityEngine.Object obj) => EditorGUIUtility.PingObject(obj);

        private void RaiseSelectionChanged() => SelectionChanged?.Invoke();
    }
}
