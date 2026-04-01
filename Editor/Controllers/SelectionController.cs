using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;
using UnityEngine;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class SelectionController : IController
    {
        private readonly ISelectionService _selectionService;
        private readonly WorkspaceState _state;
        private readonly AssetRegistry _assetRegistry;
        private bool _isSyncing;

        public SelectionController(ISelectionService selectionService, WorkspaceState state, AssetRegistry assetRegistry)
        {
            _selectionService = selectionService;
            _state = state;
            _assetRegistry = assetRegistry;
        }

        public void Initialize()
        {
            _selectionService.SelectionChanged += OnUnitySelectionChanged;
        }

        /// <summary>
        /// 用户在 CenterPanel 点击资产项时调用。
        /// 当 entry.UnityObject 为 null（mock 数据阶段）时，跳过 SetActiveObject，
        /// 避免把 Unity Editor 选择清空后触发 OnUnitySelectionChanged 回环。
        /// </summary>
        public void OnUserSelectAsset(AssetEntry entry)
        {
            if (entry == null) return;
            _isSyncing = true;
            _state.SelectedAsset.Value = entry;
            _state.UnitySelection.Value = entry.UnityObject;

            // 只有真实 UnityObject 才同步到 Unity Editor Selection
            if (entry.UnityObject != null)
                _selectionService.SetActiveObject(entry.UnityObject);

            _isSyncing = false;
        }

        /// <summary>
        /// Unity Editor Selection 变化时触发。
        /// obj 为 null 时跳过 FindByObject，防止 null 匹配所有 UnityObject==null 的 mock 资产。
        /// </summary>
        private void OnUnitySelectionChanged()
        {
            if (_isSyncing) return;
            var obj = _selectionService.GetActiveObject();
            _state.UnitySelection.Value = obj;

            // obj 为 null 说明 Editor 取消了选择，不更新 SelectedAsset
            if (obj == null) return;
            var entry = _assetRegistry.FindByObject(obj);
            if (entry != null) _state.SelectedAsset.Value = entry;
        }

        public void Dispose()
        {
            _selectionService.SelectionChanged -= OnUnitySelectionChanged;
        }
    }
}
