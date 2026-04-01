using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;
using UnityEngine;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// 执行操作的 Controller。当前仅实现 Material → Renderer 的 Apply。
    /// Instantiate / Replace / Batch Replace 等功能留作后续扩展。
    /// </summary>
    public sealed class ActionController : IController
    {
        private readonly IUndoService _undoService;
        private readonly IPrefabService _prefabService;
        private readonly ISelectionService _selectionService;
        private readonly WorkspaceState _state;

        public ActionController(IUndoService undoService, IPrefabService prefabService,
            ISelectionService selectionService, WorkspaceState state)
        {
            _undoService = undoService;
            _prefabService = prefabService;
            _selectionService = selectionService;
            _state = state;
        }

        /// <summary>
        /// 监听 SelectedAsset 与 UnitySelection 变化，发布 StatesChanged 事件。
        /// InspectorPanelView 订阅此事件来刷新按钮可用状态。
        /// </summary>
        public void Initialize()
        {
            _state.SelectedAsset.Changed += OnRelevantStateChanged;
            _state.UnitySelection.Changed += OnRelevantStateChanged;
        }

        // ── Apply to Selection ──────────────────────────────────────────

        /// <summary>
        /// 查询 Apply 按钮的可用状态与原因。
        /// View 层调用此方法来决定按钮 enabled + 提示文本，不执行任何副作用。
        ///
        /// 判定条件（按优先级）：
        /// 1. 必须在 Workspace 中选中一个资产
        /// 2. 资产类型必须是 Material
        /// 3. Unity Editor 中必须选中一个 GameObject
        /// 4. 该 GameObject 上必须有 Renderer 组件
        /// </summary>
        public (bool canApply, string reason) QueryApplyState()
        {
            var asset = _state.SelectedAsset.Value;
            if (asset == null)
                return (false, "Select an asset in the list");

            if (asset.Kind != AssetKind.Material)
                return (false, $"Only Material supported (current: {asset.Kind})");

            var target = _selectionService.GetActiveGameObject();
            if (target == null)
                return (false, "Select a GameObject in Hierarchy");

            if (target.GetComponent<Renderer>() == null)
                return (false, $"\"{target.name}\" has no Renderer");

            return (true, $"Apply \"{asset.DisplayName}\" → \"{target.name}\"");
        }

        /// <summary>
        /// 执行 Apply：将选中的 Material 赋给 Unity Selection 的 Renderer。
        /// 调用前请确认 QueryApplyState().canApply == true。
        /// </summary>
        public void ApplyToSelection()
        {
            var (canApply, reason) = QueryApplyState();
            if (!canApply)
            {
                ActionEvents.Failed.Publish(new ActionFailedEvent("ApplyToSelection", reason));
                return;
            }

            var asset = _state.SelectedAsset.Value;
            var target = _selectionService.GetActiveGameObject();
            var renderer = target.GetComponent<Renderer>();

            // Undo：记录 Renderer 的当前材质状态，支持 Ctrl+Z 回退
            _undoService.RecordObject(renderer, "Apply HMI Material");
            _prefabService.ApplyAsset(asset, target);

            var msg = $"Applied \"{asset.DisplayName}\" to \"{target.name}\"";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("ApplyToSelection", msg));
            _state.StatusMessage.Value = msg;
        }

        public void Dispose()
        {
            _state.SelectedAsset.Changed -= OnRelevantStateChanged;
            _state.UnitySelection.Changed -= OnRelevantStateChanged;
        }

        // ── 内部 ──────────────────────────────────────────────────────

        /// <summary>
        /// SelectedAsset 或 UnitySelection 任一变化时触发，
        /// 通知 View 层重新查询按钮可用状态。
        /// </summary>
        private void OnRelevantStateChanged<T>(T oldVal, T newVal)
        {
            ActionEvents.StatesChanged.Publish(new ActionStatesChangedEvent());
        }
    }
}
