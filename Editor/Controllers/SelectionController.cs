using UnityEditor;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// 选择同步控制器（Architecture Addendum Amendment 2）。
    ///
    /// 三个选择源按优先级排列：
    ///   P0 — UI 显式点击（用户在 Workspace 列表中点击资产）
    ///   P1 — Unity 外部变化（用户在 Hierarchy / Project 中选择对象）
    ///   P2 — 系统自动选择（ActionController Instantiate 后自动选中新对象）
    ///
    /// 帧锁机制防止低优先级回调覆盖高优先级操作。
    /// </summary>
    public sealed class SelectionController : IController
    {
        /// <summary>
        /// 同步状态枚举，决定当前帧内哪些选择源被接受。
        /// </summary>
        private enum SyncState
        {
            Idle,           // 无同步进行中，所有源均接受
            UserDriven,     // P0 点击处理中，阻止 P1 和 P2
            ExternalDriven, // P1 外部变化处理中，阻止 P2
        }

        private readonly ISelectionService _selectionService;
        private readonly WorkspaceState _state;
        private readonly AssetRegistry _assetRegistry;

        private SyncState _syncState = SyncState.Idle;
        private int _lockUntilFrame = -1;
        private int _frameCounter;

        public SelectionController(ISelectionService selectionService, WorkspaceState state, AssetRegistry assetRegistry)
        {
            _selectionService = selectionService;
            _state = state;
            _assetRegistry = assetRegistry;
        }

        public void Initialize()
        {
            _selectionService.SelectionChanged += OnUnitySelectionChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        // ── P0: 用户在 Workspace 列表中点击资产（最高优先级）──────────

        /// <summary>
        /// 用户在 CenterPanel 点击资产项时由 AssetGridView 调用。
        /// 设置 UserDriven 锁，持续 2 帧，阻止 P1 回调引发的选择抖动。
        /// 2 帧窗口覆盖：(1) Selection.selectionChanged 异步回调 (2) UI Toolkit 重绘。
        /// </summary>
        public void OnUserSelectAsset(AssetEntry entry)
        {
            if (entry == null) return;

            _syncState = SyncState.UserDriven;
            _lockUntilFrame = _frameCounter + 2;

            _state.SelectedAsset.Value = entry;
            _state.UnitySelection.Value = entry.UnityObject;

            // 只有真实 UnityObject 才同步到 Unity Editor Selection
            if (entry.UnityObject != null)
                _selectionService.SetActiveObject(entry.UnityObject);
        }

        // ── P1: Unity Editor 外部选择变化 ──────────────────────────

        /// <summary>
        /// Unity Editor Selection 变化时触发（Hierarchy / Project 点击）。
        /// 如果 UserDriven 锁仍在有效期内，丢弃本次回调。
        /// </summary>
        private void OnUnitySelectionChanged()
        {
            // P0 锁有效期内，丢弃 P1 回调（防抖）
            if (_syncState == SyncState.UserDriven && _frameCounter <= _lockUntilFrame)
                return;

            _syncState = SyncState.ExternalDriven;
            _lockUntilFrame = _frameCounter + 1;

            var obj = _selectionService.GetActiveObject();
            _state.UnitySelection.Value = obj;

            // obj 为 null 说明 Editor 取消了选择，不更新 SelectedAsset
            if (obj == null) return;
            var entry = _assetRegistry.FindByObject(obj);
            if (entry != null) _state.SelectedAsset.Value = entry;
        }

        // ── P2: 系统自动选择（最低优先级）──────────────────────────

        /// <summary>
        /// 系统自动选择（例如 Instantiate 后选中新对象）。
        /// 任何更高优先级的同步正在进行时，本次自动选择被丢弃。
        /// </summary>
        public void OnSystemAutoSelect(UnityEngine.Object obj)
        {
            if (_syncState != SyncState.Idle)
                return;

            // P2 不设帧锁，直接同步
            _selectionService.SetActiveObject(obj);
        }

        // ── 帧更新：释放锁 ──────────────────────────────────────

        /// <summary>
        /// 每帧递增计数器，超过锁定帧时将状态重置为 Idle。
        /// </summary>
        private void OnEditorUpdate()
        {
            _frameCounter++;
            if (_frameCounter > _lockUntilFrame && _syncState != SyncState.Idle)
                _syncState = SyncState.Idle;
        }

        public void Dispose()
        {
            _selectionService.SelectionChanged -= OnUnitySelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
        }
    }
}
