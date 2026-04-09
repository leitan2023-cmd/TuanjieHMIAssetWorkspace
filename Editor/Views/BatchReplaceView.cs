using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 批量替换工作区 — 操作闭环设计。
    ///
    /// 布局：
    /// ┌──────────────────────────────────────────┐
    /// │ 顶部上下文条（纯文字）：目标 + 影响范围     │
    /// ├────────────────────┬─────────────────────┤
    /// │ 候选材质网格        │  预览对比             │
    /// │                    │  操作按钮             │
    /// │                    │  撤销 + 历史          │
    /// └────────────────────┴─────────────────────┘
    ///
    /// 架构原则：
    ///   View 只负责展示上下文、触发操作、响应 Controller 返回结果。
    ///   所有替换逻辑（校验、Undo、Renderer 操作）由 ActionController 承担。
    /// </summary>
    public sealed class BatchReplaceView
    {
        private readonly VisualElement _root;
        private readonly BatchReplaceState _br = new();
        private readonly WorkspaceState _workspaceState;
        private readonly AssetRegistry _assetRegistry;
        private ActionController _actionController;

        // ── 上下文条 ──
        private Label _targetIcon;
        private Label _targetName;
        private Label _targetDetail;
        private Label _impactBadge;
        private VisualElement _filterRow;

        // ── 候选网格 ──
        private VisualElement _candidateGrid;

        // ── 右面板：预览 + 操作 ──
        private Image _previewBefore;
        private Image _previewAfter;
        private Label _previewBeforeLabel;
        private Label _previewAfterLabel;
        private Button _applyBtn;
        private Label _applyDesc;
        private Button _applyAllBtn;
        private Label _applyAllDesc;
        private VisualElement _historyList;
        private Button _compareBtn;

        // 异步预览
        private readonly HashSet<string> _pendingPreviews = new();

        public BatchReplaceView(VisualElement root, WorkspaceState state, AssetRegistry assetRegistry)
        {
            _root = root;
            _workspaceState = state;
            _assetRegistry = assetRegistry;
        }

        /// <summary>
        /// 绑定 View。需要传入 ActionController 以委托所有替换操作。
        /// </summary>
        public void Bind(ActionController actionController = null)
        {
            _actionController = actionController;

            if (_root == null) return;
            _root.Clear();
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.flexGrow = 1;

            BuildContextBar();
            BuildMainArea();

            // ── 响应式 ──
            _br.CurrentTarget.Changed += (_, go) => RefreshTarget(go);
            _br.CurrentMaterial.Changed += (_, mat) => RefreshCurrentMaterial(mat);
            _br.SelectedCandidate.Changed += (_, c) => RefreshSelection(c);
            _br.Candidates.Changed += (_, _) => RebuildGrid();
            _br.AffectedCount.Changed += (_, count) => RefreshImpact(count);

            // Unity Selection → 更新目标
            _workspaceState.UnitySelection.Changed += (_, obj) =>
            {
                if (obj is GameObject go)
                {
                    _br.CurrentTarget.Value = go;
                    var r = go.GetComponent<Renderer>();
                    _br.CurrentMaterial.Value = r != null ? r.sharedMaterial : null;
                    RefreshAffectedCount();
                }
            };

            // Undo/Redo 后同步 — 重新读取目标材质状态 + 影响计数
            ActionEvents.StatesChanged.Subscribe(_ => OnStatesChangedForSync());

            // 资产变化 → 重新加载候选（非活跃时仅标脏，切换时刷新）
            AssetEvents.FilteredChanged.Subscribe(_ => LoadCandidates());
            _workspaceState.CurrentViewMode.Changed += (_, mode) =>
            {
                if (mode == ViewMode.BatchReplace && _candidatesDirty)
                    LoadCandidates();
            };
            LoadCandidates();

            // 初始状态
            var sel = Selection.activeGameObject;
            if (sel != null)
            {
                _br.CurrentTarget.Value = sel;
                var r = sel.GetComponent<Renderer>();
                _br.CurrentMaterial.Value = r != null ? r.sharedMaterial : null;
                RefreshAffectedCount();
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 区块 1：顶部上下文条（纯文字）
        // ════════════════════════════════════════════════════════════════

        private void BuildContextBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("br-context-bar");

            // 目标图标 + 名称
            _targetIcon = new Label("\u25A0");
            _targetIcon.AddToClassList("br-target-icon");
            bar.Add(_targetIcon);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;

            _targetName = new Label("\u672A\u9009\u4E2D\u76EE\u6807");
            _targetName.AddToClassList("br-target-name");
            textCol.Add(_targetName);

            _targetDetail = new Label("\u5728 Hierarchy \u4E2D\u9009\u4E2D GameObject \u4EE5\u5F00\u59CB");
            _targetDetail.AddToClassList("br-target-detail");
            textCol.Add(_targetDetail);

            bar.Add(textCol);

            // 影响范围徽标
            _impactBadge = new Label("0 \u4E2A\u5BF9\u8C61\u53D7\u5F71\u54CD");
            _impactBadge.AddToClassList("br-impact-badge");
            bar.Add(_impactBadge);

            // 过滤按钮
            _filterRow = new VisualElement();
            _filterRow.AddToClassList("br-filter-row");
            var filters = new[] { "\u5168\u90E8", "\u8F66\u8EAB", "\u8F66\u8F6E", "\u706F\u5149", "\u5176\u4ED6" };
            foreach (var f in filters)
            {
                var btn = new Button { text = f };
                btn.AddToClassList("br-filter-btn");
                if (f == "\u5168\u90E8") btn.AddToClassList("active");
                var capturedBtn = btn;
                btn.RegisterCallback<ClickEvent>(_ =>
                {
                    _br.TargetFilter.Value = f;
                    foreach (var child in _filterRow.Children().OfType<Button>())
                        child.RemoveFromClassList("active");
                    capturedBtn.AddToClassList("active");
                    LoadCandidates();
                });
                _filterRow.Add(btn);
            }
            bar.Add(_filterRow);

            _root.Add(bar);
        }

        // ════════════════════════════════════════════════════════════════
        // 区块 2：主区域 — 左候选网格 | 右预览+操作闭环
        // ════════════════════════════════════════════════════════════════

        private void BuildMainArea()
        {
            var main = new VisualElement();
            main.AddToClassList("br-center-area");

            // ── 左：候选材质网格 ──
            var gridSection = new VisualElement();
            gridSection.AddToClassList("br-grid-section");

            var gridHeader = new VisualElement();
            gridHeader.AddToClassList("br-grid-header");
            var gridTitle = new Label("\u5019\u9009\u6750\u8D28");
            gridTitle.AddToClassList("br-grid-title");
            gridHeader.Add(gridTitle);
            gridSection.Add(gridHeader);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _candidateGrid = new VisualElement();
            _candidateGrid.AddToClassList("br-candidate-grid");
            scroll.Add(_candidateGrid);
            gridSection.Add(scroll);

            main.Add(gridSection);

            // ── 右：预览 + 操作闭环 ──
            var rightPanel = new VisualElement();
            rightPanel.AddToClassList("br-right-panel");

            var rightScroll = new ScrollView(ScrollViewMode.Vertical);
            rightScroll.style.flexGrow = 1;

            // 预览对比
            var previewTitle = new Label("\u9884\u89C8\u5BF9\u6BD4");
            previewTitle.AddToClassList("br-section-label");
            previewTitle.style.marginBottom = 8;
            rightScroll.Add(previewTitle);

            var previewRow = new VisualElement();
            previewRow.AddToClassList("br-preview-row");

            var beforeCol = new VisualElement();
            beforeCol.AddToClassList("br-preview-col");
            _previewBeforeLabel = new Label("\u5F53\u524D\u6750\u8D28");
            _previewBeforeLabel.AddToClassList("br-preview-label");
            beforeCol.Add(_previewBeforeLabel);
            _previewBefore = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _previewBefore.AddToClassList("br-preview-image");
            beforeCol.Add(_previewBefore);

            var arrow = new Label("\u2192");
            arrow.AddToClassList("br-preview-arrow");

            var afterCol = new VisualElement();
            afterCol.AddToClassList("br-preview-col");
            _previewAfterLabel = new Label("\u66FF\u6362\u4E3A");
            _previewAfterLabel.AddToClassList("br-preview-label");
            afterCol.Add(_previewAfterLabel);
            _previewAfter = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _previewAfter.AddToClassList("br-preview-image");
            afterCol.Add(_previewAfter);

            previewRow.Add(beforeCol);
            previewRow.Add(arrow);
            previewRow.Add(afterCol);
            rightScroll.Add(previewRow);

            // 分隔
            var sep = new VisualElement();
            sep.AddToClassList("br-separator");
            rightScroll.Add(sep);

            // 操作按钮区
            _applyBtn = new Button(OnApplySingle) { text = "\u25B6  \u5E94\u7528\u66FF\u6362" };
            _applyBtn.AddToClassList("br-apply-btn");
            _applyBtn.SetEnabled(false);
            rightScroll.Add(_applyBtn);

            _applyDesc = new Label("\u9009\u62E9\u76EE\u6807\u5BF9\u8C61\u548C\u5019\u9009\u6750\u8D28\u4EE5\u542F\u7528");
            _applyDesc.AddToClassList("br-apply-desc");
            rightScroll.Add(_applyDesc);

            _applyAllBtn = new Button(OnApplyAll) { text = "\u21BB  \u6279\u91CF\u5E94\u7528\u5230\u5168\u90E8\u5339\u914D" };
            _applyAllBtn.AddToClassList("br-apply-all-btn");
            _applyAllBtn.SetEnabled(false);
            rightScroll.Add(_applyAllBtn);

            _applyAllDesc = new Label("");
            _applyAllDesc.AddToClassList("br-apply-all-desc");
            rightScroll.Add(_applyAllDesc);

            // 对比按钮 — 进入 CompareView
            _compareBtn = new Button(OnCompare) { text = "\u2194  \u8BE6\u7EC6\u5BF9\u6BD4" };
            _compareBtn.AddToClassList("br-compare-btn");
            _compareBtn.SetEnabled(false);
            rightScroll.Add(_compareBtn);

            // 分隔
            var sep2 = new VisualElement();
            sep2.AddToClassList("br-separator");
            rightScroll.Add(sep2);

            // 撤销 + 历史
            var undoBtn = new Button(OnUndo) { text = "\u21B6  \u64A4\u9500\u4E0A\u6B21\u64CD\u4F5C" };
            undoBtn.AddToClassList("br-undo-btn");
            rightScroll.Add(undoBtn);

            var historyLabel = new Label("\u64CD\u4F5C\u5386\u53F2");
            historyLabel.AddToClassList("br-section-label");
            historyLabel.style.marginTop = 10;
            rightScroll.Add(historyLabel);

            _historyList = new VisualElement();
            rightScroll.Add(_historyList);

            rightPanel.Add(rightScroll);
            main.Add(rightPanel);
            _root.Add(main);
        }

        // ════════════════════════════════════════════════════════════════
        // Undo/Redo 同步 — 重新读取场景状态
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// ActionEvents.StatesChanged 回调（含 Undo/Redo 触发）。
        /// 仅当 BatchReplace 模式时执行，避免非活跃页面做无谓的场景查询。
        /// </summary>
        private void OnStatesChangedForSync()
        {
            // 非当前模式时跳过昂贵的场景查询
            if (_workspaceState.CurrentViewMode.Value != ViewMode.BatchReplace) return;

            using var _t = PerfTrace.Begin("BatchReplaceView.OnStatesChangedForSync");
            var target = _br.CurrentTarget.Value;
            if (target != null)
            {
                var r = target.GetComponent<Renderer>();
                var currentMat = r != null ? r.sharedMaterial : null;
                // 仅当材质确实发生变化时才更新（避免不必要的刷新循环）
                if (_br.CurrentMaterial.Value != currentMat)
                    _br.CurrentMaterial.Value = currentMat;
            }
            RefreshAffectedCount();
            RefreshActionButtons();
        }

        // ════════════════════════════════════════════════════════════════
        // 操作入口 — 委托给 ActionController
        // ════════════════════════════════════════════════════════════════

        private void OnApplySingle()
        {
            var target = _br.CurrentTarget.Value;
            var candidate = _br.SelectedCandidate.Value;
            if (target == null || candidate?.Material == null) return;

            if (_actionController != null)
            {
                var result = _actionController.ReplaceSingle(target, candidate.Material);
                AddHistory(result.Message);

                if (result.Success)
                {
                    // 更新当前材质状态以反映替换后的变化
                    _br.CurrentMaterial.Value = candidate.Material;
                    RefreshAffectedCount();
                }
            }
            else
            {
                // 降级：ActionController 未注入时的安全回退（不应发生）
                Debug.LogWarning("[BatchReplaceView] ActionController \u672A\u6CE8\u5165\uFF0C\u64CD\u4F5C\u88AB\u5FFD\u7565");
            }
        }

        private void OnApplyAll()
        {
            var currentMat = _br.CurrentMaterial.Value;
            var candidate = _br.SelectedCandidate.Value;
            if (currentMat == null || candidate?.Material == null) return;

            if (_actionController != null)
            {
                var result = _actionController.BatchReplaceAll(currentMat, candidate.Material);
                AddHistory(result.Message);

                if (result.Success)
                    RefreshAffectedCount();
            }
            else
            {
                Debug.LogWarning("[BatchReplaceView] ActionController \u672A\u6CE8\u5165\uFF0C\u64CD\u4F5C\u88AB\u5FFD\u7565");
            }
        }

        private void OnCompare()
        {
            var currentMat = _br.CurrentMaterial.Value;
            var candidate = _br.SelectedCandidate.Value;
            if (currentMat == null || candidate?.Material == null) return;

            // 写入 CompareState，由 CompareView 响应式消费
            var cs = _workspaceState.Compare;
            cs.LabelA.Value = "\u5F53\u524D\u6750\u8D28";
            cs.LabelB.Value = "\u5019\u9009\u6750\u8D28";
            cs.MaterialA.Value = currentMat;
            cs.MaterialB.Value = candidate.Material;

            // 切换到对比模式
            _workspaceState.CurrentViewMode.Value = ViewMode.Compare;
        }

        private void OnUndo()
        {
            if (_actionController != null)
                _actionController.PerformUndo();
            else
                Undo.PerformUndo();
        }

        // ════════════════════════════════════════════════════════════════
        // 数据逻辑（候选加载、影响计数 — 纯读取，不操作 Renderer）
        // ════════════════════════════════════════════════════════════════

        private bool _candidatesDirty;

        private void LoadCandidates()
        {
            // BatchReplace 非活跃时仅标记脏，切换到该视图时再实际加载
            if (_workspaceState.CurrentViewMode.Value != ViewMode.BatchReplace)
            {
                _candidatesDirty = true;
                return;
            }
            _candidatesDirty = false;

            var all = _assetRegistry.All;

            var candidates = new List<ReplacementCandidate>();
            for (int i = 0; i < all.Count; i++)
            {
                var entry = all[i];
                if (entry.Kind != AssetKind.Material) continue;
                var mat = entry.UnityObject as Material;
                if (mat == null) continue;

                // 两阶段：缓存命中时 GetAssetPreview 瞬间返回；否则 MiniThumbnail + 异步
                var fullPreview = AssetPreview.GetAssetPreview(mat);
                candidates.Add(new ReplacementCandidate
                {
                    Guid = entry.Guid,
                    Name = entry.DisplayName,
                    Path = entry.Path,
                    Material = mat,
                    Preview = fullPreview ?? AssetPreview.GetMiniThumbnail(mat),
                });

                if (fullPreview == null)
                    _pendingPreviews.Add(entry.Guid);
            }

            _br.Candidates.Value = candidates;

            if (_pendingPreviews.Count > 0)
                EditorApplication.update += PollPreviews;
        }

        private int _brPollFrame;

        private void PollPreviews()
        {
            if (_pendingPreviews.Count == 0)
            {
                EditorApplication.update -= PollPreviews;
                return;
            }

            // 节流：每 3 帧检查一次
            _brPollFrame++;
            if (_brPollFrame % 3 != 0) return;

            // 建立 GUID → Candidate 索引（首次或候选变更后）
            var candidates = _br.Candidates.Value;
            if (candidates == null) return;

            var resolved = new List<string>();
            bool anyUpdated = false;

            // 快照遍历，避免 HashSet 饥饿
            var snapshot = new List<string>(_pendingPreviews);
            for (int i = 0; i < snapshot.Count; i++)
            {
                var guid = snapshot[i];
                ReplacementCandidate candidate = null;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (candidates[j].Guid == guid) { candidate = candidates[j]; break; }
                }
                if (candidate?.Material == null)
                {
                    resolved.Add(guid);
                    continue;
                }

                var preview = AssetPreview.GetAssetPreview(candidate.Material);
                if (preview != null)
                {
                    candidate.Preview = preview;
                    resolved.Add(guid);
                    anyUpdated = true;
                }
            }

            foreach (var g in resolved)
                _pendingPreviews.Remove(g);

            // 仅在有实际更新时才刷新网格
            if (anyUpdated)
                RebuildGrid();

            if (_pendingPreviews.Count == 0)
                EditorApplication.update -= PollPreviews;
        }

        /// <summary>
        /// 通过 ActionController 查询受影响的 Renderer 数量（纯读取）。
        /// </summary>
        private void RefreshAffectedCount()
        {
            using var _t = PerfTrace.Begin("BatchReplaceView.RefreshAffectedCount");
            var currentMat = _br.CurrentMaterial.Value;
            if (currentMat == null)
            {
                _br.AffectedCount.Value = 0;
                return;
            }

            if (_actionController != null)
            {
                _br.AffectedCount.Value = _actionController.CountRenderersWithMaterial(currentMat);
            }
            else
            {
                // 降级回退（检查所有材质槽位）
                var renderers = Object.FindObjectsOfType<Renderer>();
                _br.AffectedCount.Value = renderers.Count(r =>
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == currentMat) return true;
                    return false;
                });
            }
        }

        private void AddHistory(string message)
        {
            var history = new List<string>(_br.History.Value) { $"[{System.DateTime.Now:HH:mm:ss}] {message}" };
            if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
            _br.History.Value = history;
            RefreshHistory();
        }

        // ════════════════════════════════════════════════════════════════
        // UI 刷新（保持原有结构不变）
        // ════════════════════════════════════════════════════════════════

        private void RefreshTarget(GameObject go)
        {
            if (go == null)
            {
                if (_targetName != null) _targetName.text = "\u672A\u9009\u4E2D\u76EE\u6807";
                if (_targetDetail != null) _targetDetail.text = "\u5728 Hierarchy \u4E2D\u9009\u4E2D GameObject \u4EE5\u5F00\u59CB";
                if (_targetIcon != null) _targetIcon.text = "\u25A1";
                return;
            }

            if (_targetName != null) _targetName.text = go.name;
            if (_targetIcon != null) _targetIcon.text = "\u25A0";

            var renderer = go.GetComponent<Renderer>();
            if (_targetDetail != null)
            {
                if (renderer != null)
                {
                    var matName = renderer.sharedMaterial?.name ?? "(\u65E0)";
                    var childCount = go.transform.childCount;
                    _targetDetail.text = childCount > 0
                        ? $"\u5F53\u524D\u6750\u8D28\uFF1A{matName}  \u2022  {childCount} \u4E2A\u5B50\u5BF9\u8C61"
                        : $"\u5F53\u524D\u6750\u8D28\uFF1A{matName}";
                }
                else
                {
                    _targetDetail.text = "\u8BE5\u5BF9\u8C61\u6CA1\u6709 Renderer \u7EC4\u4EF6";
                }
            }
        }

        private void RefreshCurrentMaterial(Material mat)
        {
            if (_previewBefore != null)
            {
                if (mat != null)
                {
                    var tex = AssetPreview.GetAssetPreview(mat);
                    _previewBefore.image = tex ?? AssetPreview.GetMiniThumbnail(mat);
                }
                else
                {
                    _previewBefore.image = null;
                }
            }
            if (_previewBeforeLabel != null)
                _previewBeforeLabel.text = mat != null ? mat.name : "\u5F53\u524D\u6750\u8D28";

            RefreshActionButtons();
        }

        private void RefreshSelection(ReplacementCandidate c)
        {
            if (_previewAfter != null)
                _previewAfter.image = c?.Preview;
            if (_previewAfterLabel != null)
                _previewAfterLabel.text = c != null ? c.Name : "\u66FF\u6362\u4E3A";

            RefreshActionButtons();
        }

        private void RefreshImpact(int count)
        {
            if (_impactBadge != null)
            {
                _impactBadge.text = count > 0 ? $"{count} \u4E2A\u5BF9\u8C61\u53D7\u5F71\u54CD" : "\u65E0\u53D7\u5F71\u54CD\u5BF9\u8C61";
                _impactBadge.RemoveFromClassList("impact-zero");
                _impactBadge.RemoveFromClassList("impact-active");
                _impactBadge.AddToClassList(count > 0 ? "impact-active" : "impact-zero");
            }

            RefreshActionButtons();
        }

        private void RefreshActionButtons()
        {
            using var _t = PerfTrace.Begin("BatchReplaceView.RefreshActionButtons");
            var hasTarget = _br.CurrentTarget.Value != null;
            var hasMat = _br.CurrentMaterial.Value != null;
            var hasCandidate = _br.SelectedCandidate.Value?.Material != null;

            // 通过 Controller 校验获取精确的可用状态
            bool canApply;
            bool canApplyAll;
            string applyReason = "";
            string applyAllReason = "";

            if (_actionController != null && hasTarget && hasCandidate)
            {
                var (ok, reason) = _actionController.QueryReplaceState(
                    _br.CurrentTarget.Value, _br.SelectedCandidate.Value.Material);
                canApply = ok;
                applyReason = reason;
            }
            else
            {
                canApply = hasTarget && hasCandidate;
            }

            if (_actionController != null && hasMat && hasCandidate)
            {
                var (ok, reason) = _actionController.QueryBatchReplaceState(
                    _br.CurrentMaterial.Value, _br.SelectedCandidate.Value.Material);
                canApplyAll = ok;
                applyAllReason = reason;
            }
            else
            {
                canApplyAll = hasMat && hasCandidate;
            }

            _applyBtn?.SetEnabled(canApply);
            _applyAllBtn?.SetEnabled(canApplyAll);
            _compareBtn?.SetEnabled(hasMat && hasCandidate);

            // 操作描述文字
            if (_applyDesc != null)
            {
                if (!string.IsNullOrEmpty(applyReason))
                {
                    _applyDesc.text = applyReason;
                }
                else if (!hasTarget)
                {
                    _applyDesc.text = "\u5728 Hierarchy \u4E2D\u9009\u4E2D\u76EE\u6807\u5BF9\u8C61";
                }
                else if (!hasCandidate)
                {
                    _applyDesc.text = "\u4ECE\u5019\u9009\u5217\u8868\u4E2D\u9009\u62E9\u66FF\u6362\u6750\u8D28";
                }
                else
                {
                    _applyDesc.text = "\u9009\u62E9\u76EE\u6807\u5BF9\u8C61\u548C\u5019\u9009\u6750\u8D28\u4EE5\u542F\u7528";
                }
            }

            if (_applyAllDesc != null)
            {
                if (!string.IsNullOrEmpty(applyAllReason) && canApplyAll)
                {
                    _applyAllDesc.text = applyAllReason;
                }
                else if (!string.IsNullOrEmpty(applyAllReason) && !canApplyAll && hasMat && hasCandidate)
                {
                    // 显示不可用原因（如依赖缺失）
                    _applyAllDesc.text = applyAllReason;
                }
                else
                {
                    _applyAllDesc.text = "";
                }
            }

            // ── 发布跨模式选择上下文 ──
            PublishContext();
        }

        private void PublishContext()
        {
            var target = _br.CurrentTarget.Value;
            var candidate = _br.SelectedCandidate.Value;
            var currentMat = _br.CurrentMaterial.Value;
            var count = _br.AffectedCount.Value;

            if (target == null && candidate == null)
            {
                SelectionEvents.ContextCleared.Publish(new SelectionContextClearedEvent(
                    "BatchReplace", "\u5728 Hierarchy \u4E2D\u9009\u4E2D GameObject \u4EE5\u5F00\u59CB\u6279\u91CF\u66FF\u6362"));
                return;
            }

            var title = target != null ? target.name : "\u672A\u9009\u4E2D\u76EE\u6807";
            var subtitle = "\u6279\u91CF\u66FF\u6362";
            if (currentMat != null && candidate != null)
                subtitle = $"{currentMat.name} \u2192 {candidate.Name}";
            else if (currentMat != null)
                subtitle = $"\u5F53\u524D\uFF1A{currentMat.name}  \u2022  \u9009\u62E9\u5019\u9009\u6750\u8D28";

            var detail = $"\u53D7\u5F71\u54CD\u5BF9\u8C61\uFF1A{count}";
            if (currentMat != null) detail += $"\n\u5F53\u524D\u6750\u8D28\uFF1A{currentMat.name}";
            if (candidate != null) detail += $"\n\u66FF\u6362\u4E3A\uFF1A{candidate.Name}";

            var hint = candidate != null && target != null
                ? "\u70B9\u51FB\u300C\u5E94\u7528\u66FF\u6362\u300D\u6267\u884C\u64CD\u4F5C"
                : candidate == null
                    ? "\u4ECE\u5019\u9009\u5217\u8868\u4E2D\u9009\u62E9\u66FF\u6362\u6750\u8D28"
                    : "\u5728 Hierarchy \u4E2D\u9009\u4E2D\u76EE\u6807\u5BF9\u8C61";

            SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                "BatchReplace", title, subtitle, detail, hint,
                candidate?.Preview));
        }

        private void RebuildGrid()
        {
            if (_candidateGrid == null) return;
            _candidateGrid.Clear();

            foreach (var c in _br.Candidates.Value)
            {
                var card = new VisualElement();
                card.AddToClassList("br-card");
                card.userData = c;

                var thumb = new Image
                {
                    image = c.Preview ?? AssetPreview.GetMiniThumbnail(c.Material),
                    scaleMode = ScaleMode.ScaleAndCrop,
                };
                thumb.AddToClassList("br-card-thumb");
                card.Add(thumb);

                var nameLabel = new Label(c.Name);
                nameLabel.AddToClassList("br-card-name");
                card.Add(nameLabel);

                // 匹配指示器（如果与当前材质同 shader）
                var currentMat = _br.CurrentMaterial.Value;
                if (currentMat != null && c.Material != null && c.Material.shader == currentMat.shader)
                {
                    var compat = new Label("\u540C Shader");
                    compat.AddToClassList("br-card-compat");
                    card.Add(compat);
                }

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    foreach (var child in _candidateGrid.Children())
                        child.RemoveFromClassList("selected");
                    card.AddToClassList("selected");
                    _br.SelectedCandidate.Value = c;
                });

                _candidateGrid.Add(card);
            }
        }

        private void RefreshHistory()
        {
            if (_historyList == null) return;
            _historyList.Clear();

            foreach (var entry in _br.History.Value.AsEnumerable().Reverse().Take(6))
            {
                var l = new Label(entry);
                l.AddToClassList("br-history-item");
                _historyList.Add(l);
            }
        }
    }
}
