using System.Collections.Generic;
using System.Linq;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// 执行操作的 Controller。
    /// 职责：Material → Renderer 的 Apply / Replace / BatchReplace，
    /// 统一校验、Undo 记录、状态反馈、历史日志。
    /// </summary>
    public sealed class ActionController : IController
    {
        private readonly IUndoService _undoService;
        private readonly IPrefabService _prefabService;
        private readonly ISelectionService _selectionService;
        private readonly WorkspaceState _state;
        private readonly CommandHistory _commandHistory;

        // ── 场景 Renderer 缓存 ──
        // 一次操作序列内（同一帧或同一用户交互）复用，避免多次 FindObjectsOfType
        private Renderer[] _rendererCache;
        private int _rendererCacheFrame = -1;

        public ActionController(IUndoService undoService, IPrefabService prefabService,
            ISelectionService selectionService, WorkspaceState state, CommandHistory commandHistory)
        {
            _undoService = undoService;
            _prefabService = prefabService;
            _selectionService = selectionService;
            _state = state;
            _commandHistory = commandHistory;
        }

        /// <summary>
        /// 监听 SelectedAsset 与 UnitySelection 变化，发布 StatesChanged 事件。
        /// InspectorPanelView 订阅此事件来刷新按钮可用状态。
        /// </summary>
        public void Initialize()
        {
            _state.SelectedAsset.Changed += OnRelevantStateChanged;
            _state.UnitySelection.Changed += OnRelevantStateChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        // ═══════════════════════════════════════════════════════════
        // Apply to Selection（原有能力，保持不变）
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 查询 Apply 按钮的可用状态与原因。
        /// View 层调用此方法来决定按钮 enabled + 提示文本，不执行任何副作用。
        /// </summary>
        public (bool canApply, string reason) QueryApplyState()
        {
            var asset = _state.SelectedAsset.Value;
            if (asset == null)
                return (false, "\u8BF7\u5728\u5217\u8868\u4E2D\u9009\u62E9\u4E00\u4E2A\u8D44\u4EA7");

            if (asset.Kind != AssetKind.Material)
                return (false, $"\u4EC5\u652F\u6301\u6750\u8D28\u7C7B\u578B\uFF08\u5F53\u524D\uFF1A{asset.Kind}\uFF09");

            var target = _selectionService.GetActiveGameObject();
            if (target == null)
                return (false, "\u8BF7\u5728 Hierarchy \u4E2D\u9009\u62E9\u4E00\u4E2A GameObject");

            if (target.GetComponent<Renderer>() == null)
                return (false, $"\u300C{target.name}\u300D\u6CA1\u6709 Renderer \u7EC4\u4EF6");

            return (true, $"\u5C06\u300C{asset.DisplayName}\u300D\u5E94\u7528\u5230\u300C{target.name}\u300D");
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
                _commandHistory.Add("ApplyToSelection", reason, false);
                return;
            }

            var asset = _state.SelectedAsset.Value;
            var target = _selectionService.GetActiveGameObject();
            var renderer = target.GetComponent<Renderer>();

            _undoService.RecordObject(renderer, "\u5E94\u7528 HMI \u6750\u8D28");
            _prefabService.ApplyAsset(asset, target);

            var msg = $"\u5DF2\u5C06\u300C{asset.DisplayName}\u300D\u5E94\u7528\u5230\u300C{target.name}\u300D";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("ApplyToSelection", msg));
            _commandHistory.Add("ApplyToSelection", msg, true);
            _state.StatusMessage.Value = msg;
        }

        // ═══════════════════════════════════════════════════════════
        // 批量替换 — 单个对象替换
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 校验单个替换操作是否可执行。
        /// </summary>
        public (bool canReplace, string reason) QueryReplaceState(
            GameObject target, Material newMaterial)
        {
            if (target == null)
                return (false, "\u76EE\u6807\u5BF9\u8C61\u4E3A\u7A7A");

            if (newMaterial == null)
                return (false, "\u66FF\u6362\u6750\u8D28\u65E0\u6548");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return (false, $"\u300C{target.name}\u300D\u6CA1\u6709 Renderer \u7EC4\u4EF6\uFF0C\u65E0\u6CD5\u66FF\u6362\u6750\u8D28");

            // 依赖状态校验：Core + ShaderLibrary 必须可用
            if (_state.CoreHealth.Value != PackageHealth.Installed)
                return (false, "HMIRP Core \u672A\u5B89\u88C5\uFF0C\u6750\u8D28\u66FF\u6362\u4E0D\u53EF\u7528");

            if (_state.ShaderLibraryHealth.Value != PackageHealth.Installed)
                return (false, "Shader Library \u672A\u5B89\u88C5\uFF0C\u6750\u8D28\u53EF\u80FD\u65E0\u6CD5\u6B63\u786E\u6E32\u67D3");

            // 检查所有槽位中是否有可替换的材质
            var currentMat = renderer.sharedMaterial;
            if (currentMat == newMaterial)
                return (false, "\u65B0\u6750\u8D28\u4E0E\u5F53\u524D\u6750\u8D28\u76F8\u540C\uFF0C\u65E0\u9700\u66FF\u6362");

            var oldName = currentMat != null ? currentMat.name : "(\u65E0)";
            int slotCount = renderer.sharedMaterials.Count(m => m == currentMat);
            var slotHint = slotCount > 1 ? $"({slotCount} \u4E2A\u69FD\u4F4D)" : "";
            return (true, $"\u5C06\u300C{target.name}\u300D\u7684\u300C{oldName}\u300D\u66FF\u6362\u4E3A\u300C{newMaterial.name}\u300D{slotHint}");
        }

        /// <summary>
        /// 对单个目标对象执行材质替换。
        /// 支持多材质槽：只替换与 currentMaterial 匹配的槽位。
        /// </summary>
        public ReplaceResult ReplaceSingle(GameObject target, Material newMaterial)
        {
            var (canReplace, reason) = QueryReplaceState(target, newMaterial);
            if (!canReplace)
            {
                ActionEvents.Failed.Publish(new ActionFailedEvent("ReplaceSingle", reason));
                _commandHistory.Add("ReplaceSingle", reason, false);
                return new ReplaceResult(false, 0, reason);
            }

            var renderer = target.GetComponent<Renderer>();
            var oldMats = renderer.sharedMaterials;
            var currentMat = renderer.sharedMaterial; // 主槽材质作为匹配目标
            int slotsReplaced = 0;

            _undoService.SetGroupName("\u5355\u4E2A\u66FF\u6362\u6750\u8D28");
            _undoService.RecordObject(renderer, "\u5355\u4E2A\u66FF\u6362\u6750\u8D28");

            // 多槽替换：遍历所有槽位，替换匹配的
            var newMats = new Material[oldMats.Length];
            for (int i = 0; i < oldMats.Length; i++)
            {
                if (oldMats[i] == currentMat)
                {
                    newMats[i] = newMaterial;
                    slotsReplaced++;
                }
                else
                {
                    newMats[i] = oldMats[i];
                }
            }
            renderer.sharedMaterials = newMats;
            InvalidateRendererCache();

            var oldName = currentMat != null ? currentMat.name : "(\u65E0)";
            var slotInfo = slotsReplaced > 1 ? $"({slotsReplaced} \u4E2A\u69FD\u4F4D)" : "";
            var msg = $"\u2713 \u5DF2\u5C06\u300C{target.name}\u300D\u7684\u300C{oldName}\u300D\u66FF\u6362\u4E3A\u300C{newMaterial.name}\u300D{slotInfo}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("ReplaceSingle", msg));
            _commandHistory.Add("ReplaceSingle", msg, true);
            _state.StatusMessage.Value = msg;

            return new ReplaceResult(true, 1, msg);
        }

        // ═══════════════════════════════════════════════════════════
        // 批量替换 — 所有匹配对象
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 校验批量替换操作是否可执行。
        /// </summary>
        public (bool canReplace, string reason) QueryBatchReplaceState(
            Material currentMaterial, Material newMaterial)
        {
            if (currentMaterial == null)
                return (false, "\u5F53\u524D\u6750\u8D28\u4E3A\u7A7A");

            if (newMaterial == null)
                return (false, "\u66FF\u6362\u6750\u8D28\u65E0\u6548");

            if (currentMaterial == newMaterial)
                return (false, "\u65B0\u6750\u8D28\u4E0E\u5F53\u524D\u6750\u8D28\u76F8\u540C\uFF0C\u65E0\u9700\u66FF\u6362");

            // 依赖状态校验
            if (_state.CoreHealth.Value != PackageHealth.Installed)
                return (false, "HMIRP Core \u672A\u5B89\u88C5\uFF0C\u6750\u8D28\u66FF\u6362\u4E0D\u53EF\u7528");

            if (_state.ShaderLibraryHealth.Value != PackageHealth.Installed)
                return (false, "Shader Library \u672A\u5B89\u88C5\uFF0C\u6750\u8D28\u53EF\u80FD\u65E0\u6CD5\u6B63\u786E\u6E32\u67D3");

            // 查找所有匹配的 Renderer
            var matchCount = CountRenderersWithMaterial(currentMaterial);
            if (matchCount == 0)
                return (false, $"\u573A\u666F\u4E2D\u6CA1\u6709\u4F7F\u7528\u300C{currentMaterial.name}\u300D\u7684\u5BF9\u8C61");

            return (true, $"\u5C06\u573A\u666F\u4E2D {matchCount} \u4E2A\u300C{currentMaterial.name}\u300D\u66FF\u6362\u4E3A\u300C{newMaterial.name}\u300D");
        }

        /// <summary>
        /// 对场景中所有使用 currentMaterial 的 Renderer 批量替换为 newMaterial。
        /// 支持多材质槽：只替换与 currentMaterial 匹配的槽位。
        /// 所有修改合并为单次 Undo 组。
        /// </summary>
        public ReplaceResult BatchReplaceAll(Material currentMaterial, Material newMaterial)
        {
            var (canReplace, reason) = QueryBatchReplaceState(currentMaterial, newMaterial);
            if (!canReplace)
            {
                ActionEvents.Failed.Publish(new ActionFailedEvent("BatchReplaceAll", reason));
                _commandHistory.Add("BatchReplaceAll", reason, false);
                return new ReplaceResult(false, 0, reason);
            }

            var renderers = FindRenderersWithMaterial(currentMaterial);
            int totalSlots = 0;

            _undoService.SetGroupName("\u6279\u91CF\u66FF\u6362\u5168\u90E8\u6750\u8D28");
            foreach (var r in renderers)
            {
                _undoService.RecordObject(r, "\u6279\u91CF\u66FF\u6362\u6750\u8D28");
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == currentMaterial)
                    {
                        mats[i] = newMaterial;
                        totalSlots++;
                        changed = true;
                    }
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            InvalidateRendererCache();

            var slotInfo = totalSlots > renderers.Length ? $"\uFF08\u5171 {totalSlots} \u4E2A\u69FD\u4F4D\uFF09" : "";
            var msg = $"\u2713 \u5DF2\u5C06 {renderers.Length} \u4E2A\u5BF9\u8C61\u7684\u300C{currentMaterial.name}\u300D\u66FF\u6362\u4E3A\u300C{newMaterial.name}\u300D{slotInfo}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("BatchReplaceAll", msg));
            _commandHistory.Add("BatchReplaceAll", msg, true);
            _state.StatusMessage.Value = msg;

            return new ReplaceResult(true, renderers.Length, msg);
        }

        // ═══════════════════════════════════════════════════════════
        // 辅助查询（供 View 和 Controller 自身使用）
        // ═══════════════════════════════════════════════════════════

        /// <summary>统计场景中使用指定材质的 Renderer 数量（检查所有槽位）。</summary>
        public int CountRenderersWithMaterial(Material material)
        {
            using var _t = Core.PerfTrace.Begin("ActionController.CountRenderersWithMaterial");
            if (material == null) return 0;
            var renderers = GetCachedRenderers();
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (ContainsMaterial(renderers[i], material))
                    count++;
            }
            return count;
        }

        // ═══════════════════════════════════════════════════════════
        // CompareView — 应用 B 到场景（统一 Undo 语义）
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 将材质应用到 GameObject 的 Renderer（供 CompareView 调用）。
        /// 统一 Undo 分组 + CommandHistory 日志。
        /// </summary>
        public ReplaceResult CompareApply(GameObject target, Material material)
        {
            if (target == null)
            {
                var msg = "\u76EE\u6807\u5BF9\u8C61\u4E3A\u7A7A";
                ActionEvents.Failed.Publish(new ActionFailedEvent("CompareApply", msg));
                _commandHistory.Add("CompareApply", msg, false);
                return new ReplaceResult(false, 0, msg);
            }

            if (material == null)
            {
                var msg = "\u6750\u8D28\u4E3A\u7A7A";
                ActionEvents.Failed.Publish(new ActionFailedEvent("CompareApply", msg));
                _commandHistory.Add("CompareApply", msg, false);
                return new ReplaceResult(false, 0, msg);
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                var msg = $"\u300C{target.name}\u300D\u6CA1\u6709 Renderer \u7EC4\u4EF6";
                ActionEvents.Failed.Publish(new ActionFailedEvent("CompareApply", msg));
                _commandHistory.Add("CompareApply", msg, false);
                return new ReplaceResult(false, 0, msg);
            }

            var oldName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "(\u65E0)";

            _undoService.SetGroupName("\u5BF9\u6BD4\u5E94\u7528\u6750\u8D28");
            _undoService.RecordObject(renderer, "\u5BF9\u6BD4\u5E94\u7528\u6750\u8D28");
            renderer.sharedMaterial = material;

            var successMsg = $"\u2713 \u5DF2\u5C06\u300C{target.name}\u300D\u7684\u6750\u8D28\u4ECE\u300C{oldName}\u300D\u66FF\u6362\u4E3A\u300C{material.name}\u300D";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("CompareApply", successMsg));
            _commandHistory.Add("CompareApply", successMsg, true);
            _state.StatusMessage.Value = $"\u5DF2\u5E94\u7528 {material.name} \u5230 {target.name}";

            return new ReplaceResult(true, 1, successMsg);
        }

        /// <summary>执行一次 Undo 操作（供 View 调用，不直接依赖 UnityEditor.Undo）。</summary>
        public void PerformUndo()
        {
            _undoService.PerformUndo();
        }

        // ═══════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════

        public void Dispose()
        {
            _state.SelectedAsset.Changed -= OnRelevantStateChanged;
            _state.UnitySelection.Changed -= OnRelevantStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        // ── 内部 ──

        // 防止同一帧内多次 Observable 变化导致重复 Publish
        private int _lastStatesChangedFrame = -1;

        private void OnRelevantStateChanged<T>(T oldVal, T newVal)
        {
            int frame = UnityEngine.Time.frameCount;
            if (_lastStatesChangedFrame == frame) return; // 同帧去重
            _lastStatesChangedFrame = frame;
            ActionEvents.StatesChanged.Publish(new ActionStatesChangedEvent());
        }

        /// <summary>
        /// Unity Undo/Redo 回调 — 材质等场景对象可能已被还原，
        /// 通知所有 View 重新读取状态。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            InvalidateRendererCache();
            _state.StatusMessage.Value = "Undo/Redo 已执行，正在刷新…";
            ActionEvents.StatesChanged.Publish(new ActionStatesChangedEvent());
        }

        /// <summary>
        /// 获取缓存的场景 Renderer 数组。同一帧内只做一次 FindObjectsOfType。
        /// Undo/操作执行后自动失效（通过帧号判断）。
        /// </summary>
        private Renderer[] GetCachedRenderers()
        {
            int frame = UnityEngine.Time.frameCount;
            if (_rendererCache == null || _rendererCacheFrame != frame)
            {
                _rendererCache = Object.FindObjectsOfType<Renderer>();
                _rendererCacheFrame = frame;
            }
            return _rendererCache;
        }

        /// <summary>显式使缓存失效（操作执行后调用）。</summary>
        public void InvalidateRendererCache()
        {
            _rendererCache = null;
            _rendererCacheFrame = -1;
        }

        private Renderer[] FindRenderersWithMaterial(Material material)
        {
            return GetCachedRenderers()
                .Where(r => ContainsMaterial(r, material))
                .ToArray();
        }

        /// <summary>检查 Renderer 的 sharedMaterials 数组中是否包含指定材质。</summary>
        private static bool ContainsMaterial(Renderer r, Material material)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == material) return true;
            }
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 替换操作结果
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 替换操作的返回结果，供 View 层消费以刷新 UI。
    /// </summary>
    public readonly struct ReplaceResult
    {
        public ReplaceResult(bool success, int affectedCount, string message)
        {
            Success = success;
            AffectedCount = affectedCount;
            Message = message;
        }

        /// <summary>操作是否成功</summary>
        public bool Success { get; }
        /// <summary>受影响的 Renderer 数量</summary>
        public int AffectedCount { get; }
        /// <summary>结果消息（成功描述或失败原因）</summary>
        public string Message { get; }
    }
}
