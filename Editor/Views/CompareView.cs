using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// 材质 A/B 对比工作区。
    ///
    /// 布局：
    /// ┌──────────────────────────────────────────────────┐
    /// │  顶部：对比模式标题 + 来源说明 + 操作按钮         │
    /// ├─────────────────────┬────────────────────────────┤
    /// │  A 侧（当前材质）    │  B 侧（候选材质）          │
    /// │  ┌───────────┐      │  ┌───────────┐            │
    /// │  │  预览图    │      │  │  预览图    │            │
    /// │  └───────────┘      │  └───────────┘            │
    /// │  名称 / Shader      │  名称 / Shader            │
    /// │  属性摘要           │  属性摘要                  │
    /// ├─────────────────────┴────────────────────────────┤
    /// │  差异摘要区                                       │
    /// └──────────────────────────────────────────────────┘
    ///
    /// 进入路径：
    ///   1. BatchReplace → 当前材质 vs 选中候选 → 自动填充 A/B
    ///   2. AssetGrid 选中材质资产 + Hierarchy 当前选中 → 填充 A/B
    ///   3. 空状态 → 引导用户选择
    /// </summary>
    public sealed class CompareView
    {
        private readonly VisualElement _root;
        private WorkspaceState _state;
        private CompareState _cs;
        private ActionController _actionController;

        // ── 顶部 ──
        private Label _sourceHint;

        // ── A 侧 ──
        private Image _previewA;
        private Label _nameA;
        private Label _shaderA;
        private VisualElement _propsA;
        private Label _tagA;

        // ── B 侧 ──
        private Image _previewB;
        private Label _nameB;
        private Label _shaderB;
        private VisualElement _propsB;
        private Label _tagB;

        // ── 差异摘要 ──
        private VisualElement _diffSection;
        private Label _diffLabel;

        // ── 空状态 ──
        private VisualElement _emptyState;
        private VisualElement _compareBody;

        // ── 操作 ──
        private Button _swapBtn;
        private Button _applyBBtn;

        // 异步预览轮询
        private Material _pollMatA;
        private Material _pollMatB;

        public CompareView(VisualElement root) => _root = root;

        /// <summary>注入 ActionController 以统一 Undo/History 语义（可选，Bind 前后均可调用）。</summary>
        public void SetActionController(ActionController controller) => _actionController = controller;

        public void Bind(WorkspaceState state)
        {
            if (_root == null) return;
            _state = state;
            _cs = state.Compare;
            _root.Clear();

            var container = new VisualElement();
            container.AddToClassList("cmp-container");

            BuildTopBar(container);
            BuildEmptyState(container);
            BuildCompareBody(container);
            BuildDiffSection(container);

            _root.Add(container);

            // ── 响应式绑定 ──
            _cs.MaterialA.Changed += (_, mat) => RefreshSideA(mat);
            _cs.MaterialB.Changed += (_, mat) => RefreshSideB(mat);
            _cs.PreviewA.Changed += (_, tex) => { if (_previewA != null) _previewA.image = tex; };
            _cs.PreviewB.Changed += (_, tex) => { if (_previewB != null) _previewB.image = tex; };
            _cs.LabelA.Changed += (_, l) => { if (_tagA != null) _tagA.text = l; };
            _cs.LabelB.Changed += (_, l) => { if (_tagB != null) _tagB.text = l; };
            _cs.DiffSummary.Changed += (_, s) => RefreshDiff(s);

            // ── 自动填充：从 BatchReplace 流入 ──
            // 当 ViewMode 切换到 Compare，从现有状态尝试填充
            state.CurrentViewMode.Changed += (_, mode) =>
            {
                if (mode == ViewMode.Compare)
                    TryAutoFill();
            };

            // 当 SelectedAsset 变化且处于 Compare 模式 → 更新 B 侧
            state.SelectedAsset.Changed += (_, asset) =>
            {
                if (state.CurrentViewMode.Value != ViewMode.Compare) return;
                if (asset?.UnityObject is Material mat)
                    SetSideB(mat, asset.DisplayName, "\u8D44\u4EA7\u6D4F\u89C8");
            };

            // UnitySelection 变化且处于 Compare 模式 → 更新 A 侧
            state.UnitySelection.Changed += (_, obj) =>
            {
                if (state.CurrentViewMode.Value != ViewMode.Compare) return;
                if (obj is GameObject go)
                {
                    var r = go.GetComponent<Renderer>();
                    if (r != null && r.sharedMaterial != null)
                        SetSideA(r.sharedMaterial, go.name, "Hierarchy \u9009\u4E2D");
                }
            };

            // Undo/Redo 后同步 — 重新读取 A 侧材质
            ActionEvents.StatesChanged.Subscribe(_ => OnUndoRedoSync());

            // 初始状态
            RefreshVisibility();
        }

        // ════════════════════════════════════════════════════════════════
        // Undo/Redo 同步 — 重新读取 A 侧材质
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Undo/Redo 后从 Hierarchy 当前选中物体重新读取 A 侧材质。
        /// 仅在 Compare 模式下执行，避免无关页面开销。
        /// </summary>
        private void OnUndoRedoSync()
        {
            if (_state == null || _state.CurrentViewMode.Value != ViewMode.Compare) return;

            var sel = _state.UnitySelection.Value;
            if (sel is GameObject go)
            {
                var r = go.GetComponent<Renderer>();
                var sceneMat = r != null ? r.sharedMaterial : null;

                // A 侧材质已改变（Undo 还原了场景 Renderer）→ 刷新
                if (sceneMat != _cs.MaterialA.Value)
                    SetSideA(sceneMat, go.name, "Hierarchy 选中");
            }

            // B 侧保持不变（资产引用不受 Undo 影响），但刷新应用按钮状态
            _applyBBtn?.SetEnabled(
                _cs.MaterialB.Value != null && _state.UnitySelection.Value is GameObject);
        }

        // ════════════════════════════════════════════════════════════════
        // 公开 API — 供外部设置对比对象
        // ════════════════════════════════════════════════════════════════

        /// <summary>设置 A 侧材质。</summary>
        public void SetSideA(Material mat, string label, string source)
        {
            _cs.MaterialA.Value = mat;
            _cs.LabelA.Value = source ?? "\u5F53\u524D\u6750\u8D28";
            RequestPreview(mat, true);
            RefreshVisibility();
            ComputeDiff();
            PublishContext();
        }

        /// <summary>设置 B 侧材质。</summary>
        public void SetSideB(Material mat, string label, string source)
        {
            _cs.MaterialB.Value = mat;
            _cs.LabelB.Value = source ?? "\u5019\u9009\u6750\u8D28";
            RequestPreview(mat, false);
            RefreshVisibility();
            ComputeDiff();
            PublishContext();
        }

        /// <summary>一次性设置两侧（从 BatchReplace 进入时使用）。</summary>
        public void SetPair(Material a, string labelA, Material b, string labelB)
        {
            _cs.LabelA.Value = labelA;
            _cs.LabelB.Value = labelB;
            _cs.MaterialA.Value = a;
            _cs.MaterialB.Value = b;
            RequestPreview(a, true);
            RequestPreview(b, false);
            RefreshVisibility();
            ComputeDiff();
            PublishContext();
        }

        // ════════════════════════════════════════════════════════════════
        // 顶部栏
        // ════════════════════════════════════════════════════════════════

        private void BuildTopBar(VisualElement parent)
        {
            var bar = new VisualElement();
            bar.AddToClassList("cmp-top-bar");

            var title = new Label("\u6750\u8D28\u5BF9\u6BD4");
            title.AddToClassList("cmp-title");
            bar.Add(title);

            _sourceHint = new Label("");
            _sourceHint.AddToClassList("cmp-source-hint");
            bar.Add(_sourceHint);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            _swapBtn = new Button(OnSwap) { text = "\u21C4 \u4EA4\u6362 A/B" };
            _swapBtn.AddToClassList("cmp-swap-btn");
            bar.Add(_swapBtn);

            _applyBBtn = new Button(OnApplyB) { text = "\u25B6 \u5E94\u7528 B \u5230\u573A\u666F" };
            _applyBBtn.AddToClassList("cmp-apply-btn");
            _applyBBtn.SetEnabled(false);
            bar.Add(_applyBBtn);

            parent.Add(bar);
        }

        // ════════════════════════════════════════════════════════════════
        // 空状态
        // ════════════════════════════════════════════════════════════════

        private void BuildEmptyState(VisualElement parent)
        {
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("cmp-empty");

            var icon = new Label("\u2194");
            icon.AddToClassList("cmp-empty-icon");
            _emptyState.Add(icon);

            var title = new Label("\u8FD8\u6CA1\u6709\u5BF9\u6BD4\u5BF9\u8C61");
            title.AddToClassList("cmp-empty-title");
            _emptyState.Add(title);

            var steps = new VisualElement();
            steps.AddToClassList("cmp-empty-steps");

            steps.Add(CreateGuideStep("1",
                "\u4ECE\u6279\u91CF\u66FF\u6362\u8FDB\u5165",
                "\u5728\u6279\u91CF\u66FF\u6362\u4E2D\u9009\u4E2D\u5019\u9009\u6750\u8D28\u540E\uFF0C\u70B9\u51FB\u201C\u8BE6\u7EC6\u5BF9\u6BD4\u201D\u6309\u94AE"));
            steps.Add(CreateGuideStep("2",
                "\u4ECE\u8D44\u4EA7\u6D4F\u89C8\u8FDB\u5165",
                "\u5728\u8D44\u4EA7\u5217\u8868\u53F3\u952E\u6750\u8D28 \u2192 \u300C\u5BF9\u6BD4 \u2192 \u8BBE\u4E3A A/B \u4FA7\u300D\uFF0C\u6216 Hierarchy \u9009\u4E2D\u5BF9\u8C61\u540E\u5207\u6362\u5230\u6B64\u6A21\u5F0F"));

            _emptyState.Add(steps);
            parent.Add(_emptyState);
        }

        private static VisualElement CreateGuideStep(string number, string title, string desc)
        {
            var step = new VisualElement();
            step.AddToClassList("cmp-guide-step");

            var numLabel = new Label(number);
            numLabel.AddToClassList("cmp-guide-num");
            step.Add(numLabel);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("cmp-guide-title");
            textCol.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("cmp-guide-desc");
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(descLabel);

            step.Add(textCol);
            return step;
        }

        // ════════════════════════════════════════════════════════════════
        // 对比主体：A | vs | B
        // ════════════════════════════════════════════════════════════════

        private void BuildCompareBody(VisualElement parent)
        {
            _compareBody = new VisualElement();
            _compareBody.AddToClassList("cmp-body");
            _compareBody.style.display = DisplayStyle.None;

            // ── A 侧 ──
            var sideA = BuildSide(
                out _tagA, out _previewA, out _nameA, out _shaderA, out _propsA,
                "\u5F53\u524D\u6750\u8D28", "cmp-side-a");
            _compareBody.Add(sideA);

            // ── 中间分隔 ──
            var divider = new VisualElement();
            divider.AddToClassList("cmp-divider");
            var vsLabel = new Label("VS");
            vsLabel.AddToClassList("cmp-vs-label");
            divider.Add(vsLabel);
            _compareBody.Add(divider);

            // ── B 侧 ──
            var sideB = BuildSide(
                out _tagB, out _previewB, out _nameB, out _shaderB, out _propsB,
                "\u5019\u9009\u6750\u8D28", "cmp-side-b");
            _compareBody.Add(sideB);

            parent.Add(_compareBody);
        }

        private static VisualElement BuildSide(
            out Label tag, out Image preview, out Label nameLabel,
            out Label shaderLabel, out VisualElement propsContainer,
            string defaultTag, string sideClass)
        {
            var side = new VisualElement();
            side.AddToClassList("cmp-side");
            side.AddToClassList(sideClass);

            // 来源标签
            tag = new Label(defaultTag);
            tag.AddToClassList("cmp-side-tag");
            side.Add(tag);

            // 预览图
            var previewWrap = new VisualElement();
            previewWrap.AddToClassList("cmp-preview-wrap");

            preview = new Image();
            preview.AddToClassList("cmp-preview");
            preview.scaleMode = ScaleMode.ScaleAndCrop;
            previewWrap.Add(preview);
            side.Add(previewWrap);

            // 名称
            nameLabel = new Label("\u672A\u9009\u62E9");
            nameLabel.AddToClassList("cmp-mat-name");
            side.Add(nameLabel);

            // Shader
            shaderLabel = new Label("");
            shaderLabel.AddToClassList("cmp-mat-shader");
            side.Add(shaderLabel);

            // 属性列表
            propsContainer = new VisualElement();
            propsContainer.AddToClassList("cmp-props");
            side.Add(propsContainer);

            return side;
        }

        // ════════════════════════════════════════════════════════════════
        // 差异摘要区
        // ════════════════════════════════════════════════════════════════

        private void BuildDiffSection(VisualElement parent)
        {
            _diffSection = new VisualElement();
            _diffSection.AddToClassList("cmp-diff-section");
            _diffSection.style.display = DisplayStyle.None;

            var diffTitle = new Label("\u5DEE\u5F02\u6458\u8981");
            diffTitle.AddToClassList("cmp-diff-title");
            _diffSection.Add(diffTitle);

            _diffLabel = new Label("");
            _diffLabel.AddToClassList("cmp-diff-body");
            _diffLabel.style.whiteSpace = WhiteSpace.Normal;
            _diffSection.Add(_diffLabel);

            parent.Add(_diffSection);
        }

        // ════════════════════════════════════════════════════════════════
        // 刷新逻辑
        // ════════════════════════════════════════════════════════════════

        private void RefreshSideA(Material mat)
        {
            if (_nameA != null) _nameA.text = mat != null ? mat.name : "\u672A\u9009\u62E9";
            if (_shaderA != null) _shaderA.text = mat != null ? mat.shader.name : "";
            RebuildProps(_propsA, mat);
        }

        private void RefreshSideB(Material mat)
        {
            if (_nameB != null) _nameB.text = mat != null ? mat.name : "\u672A\u9009\u62E9";
            if (_shaderB != null) _shaderB.text = mat != null ? mat.shader.name : "";
            RebuildProps(_propsB, mat);

            // B 侧有值时启用应用按钮
            _applyBBtn?.SetEnabled(mat != null && _state?.UnitySelection.Value is GameObject);
        }

        private static void RebuildProps(VisualElement container, Material mat)
        {
            if (container == null) return;
            container.Clear();

            if (mat == null) return;

            // 提取关键可视化属性
            AddPropRow(container, "\u6E32\u67D3\u6A21\u5F0F", mat.renderQueue <= 2500 ? "Opaque" : "Transparent");

            if (mat.HasProperty("_Color"))
            {
                var c = mat.GetColor("_Color");
                AddPropColorRow(container, "\u4E3B\u989C\u8272", c);
            }

            if (mat.HasProperty("_Metallic"))
                AddPropRow(container, "\u91D1\u5C5E\u5EA6", mat.GetFloat("_Metallic").ToString("F2"));

            if (mat.HasProperty("_Glossiness"))
                AddPropRow(container, "\u5149\u6ED1\u5EA6", mat.GetFloat("_Glossiness").ToString("F2"));
            else if (mat.HasProperty("_Smoothness"))
                AddPropRow(container, "\u5149\u6ED1\u5EA6", mat.GetFloat("_Smoothness").ToString("F2"));

            if (mat.HasProperty("_MainTex"))
            {
                var tex = mat.GetTexture("_MainTex");
                AddPropRow(container, "\u4E3B\u8D34\u56FE", tex != null ? tex.name : "(\u65E0)");
            }

            if (mat.HasProperty("_BumpMap"))
            {
                var tex = mat.GetTexture("_BumpMap");
                AddPropRow(container, "\u6CD5\u7EBF\u8D34\u56FE", tex != null ? tex.name : "(\u65E0)");
            }

            bool hasEmission = mat.IsKeywordEnabled("_EMISSION");
            AddPropRow(container, "\u81EA\u53D1\u5149", hasEmission ? "\u5F00\u542F" : "\u5173\u95ED");
        }

        private static void AddPropRow(VisualElement parent, string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("cmp-prop-row");

            var k = new Label(key);
            k.AddToClassList("cmp-prop-key");
            row.Add(k);

            var v = new Label(value);
            v.AddToClassList("cmp-prop-value");
            row.Add(v);

            parent.Add(row);
        }

        private static void AddPropColorRow(VisualElement parent, string key, Color color)
        {
            var row = new VisualElement();
            row.AddToClassList("cmp-prop-row");

            var k = new Label(key);
            k.AddToClassList("cmp-prop-key");
            row.Add(k);

            var swatch = new VisualElement();
            swatch.AddToClassList("cmp-color-swatch");
            swatch.style.backgroundColor = new StyleColor(color);
            row.Add(swatch);

            var v = new Label($"R{color.r:F2} G{color.g:F2} B{color.b:F2}");
            v.AddToClassList("cmp-prop-value");
            row.Add(v);

            parent.Add(row);
        }

        private void RefreshVisibility()
        {
            bool hasPair = _cs.MaterialA.Value != null || _cs.MaterialB.Value != null;
            if (_emptyState != null)
                _emptyState.style.display = hasPair ? DisplayStyle.None : DisplayStyle.Flex;
            if (_compareBody != null)
                _compareBody.style.display = hasPair ? DisplayStyle.Flex : DisplayStyle.None;
            if (_diffSection != null)
                _diffSection.style.display = (_cs.MaterialA.Value != null && _cs.MaterialB.Value != null)
                    ? DisplayStyle.Flex : DisplayStyle.None;

            // 来源提示
            if (_sourceHint != null)
            {
                if (!hasPair)
                    _sourceHint.text = "";
                else if (_cs.MaterialA.Value != null && _cs.MaterialB.Value != null)
                    _sourceHint.text = $"{_cs.LabelA.Value}  \u2194  {_cs.LabelB.Value}";
                else if (_cs.MaterialA.Value != null)
                    _sourceHint.text = $"A: {_cs.MaterialA.Value.name}\u2014\u2014\u8BF7\u9009\u62E9 B \u4FA7\u6750\u8D28";
                else
                    _sourceHint.text = $"B: {_cs.MaterialB.Value.name}\u2014\u2014\u8BF7\u9009\u62E9 A \u4FA7\u6750\u8D28";
            }
        }

        private void RefreshDiff(string summary)
        {
            if (_diffLabel != null) _diffLabel.text = summary;
        }

        // ════════════════════════════════════════════════════════════════
        // 差异计算
        // ════════════════════════════════════════════════════════════════

        private void ComputeDiff()
        {
            using var _t = PerfTrace.Begin("CompareView.ComputeDiff");
            var a = _cs.MaterialA.Value;
            var b = _cs.MaterialB.Value;

            if (a == null || b == null)
            {
                _cs.DiffSummary.Value = "";
                return;
            }

            var diffs = new List<string>();

            // Shader
            if (a.shader.name != b.shader.name)
                diffs.Add($"Shader: {a.shader.name} \u2192 {b.shader.name}");

            // Render queue
            if (a.renderQueue != b.renderQueue)
                diffs.Add($"\u6E32\u67D3\u961F\u5217: {a.renderQueue} \u2192 {b.renderQueue}");

            // Color
            CompareFloat(a, b, "_Color", "\u4E3B\u989C\u8272", diffs,
                m => { var c = m.GetColor("_Color"); return $"({c.r:F2},{c.g:F2},{c.b:F2})"; });

            CompareFloat(a, b, "_Metallic", "\u91D1\u5C5E\u5EA6", diffs,
                m => m.GetFloat("_Metallic").ToString("F2"));

            CompareFloat(a, b, "_Glossiness", "\u5149\u6ED1\u5EA6", diffs,
                m => m.GetFloat("_Glossiness").ToString("F2"));

            CompareTexture(a, b, "_MainTex", "\u4E3B\u8D34\u56FE", diffs);
            CompareTexture(a, b, "_BumpMap", "\u6CD5\u7EBF\u8D34\u56FE", diffs);

            // Emission
            bool emA = a.IsKeywordEnabled("_EMISSION");
            bool emB = b.IsKeywordEnabled("_EMISSION");
            if (emA != emB)
                diffs.Add($"\u81EA\u53D1\u5149: {(emA ? "\u5F00" : "\u5173")} \u2192 {(emB ? "\u5F00" : "\u5173")}");

            if (diffs.Count == 0)
                _cs.DiffSummary.Value = "\u2713 \u4E24\u4E2A\u6750\u8D28\u5173\u952E\u5C5E\u6027\u4E00\u81F4\uFF0C\u65E0\u660E\u663E\u5DEE\u5F02";
            else
                _cs.DiffSummary.Value = string.Join("\n", diffs);
        }

        private static void CompareFloat(Material a, Material b, string prop, string label,
            List<string> diffs, System.Func<Material, string> formatter)
        {
            bool aHas = a.HasProperty(prop);
            bool bHas = b.HasProperty(prop);
            if (!aHas && !bHas) return;
            if (aHas != bHas)
            {
                diffs.Add($"{label}: {(aHas ? formatter(a) : "N/A")} \u2192 {(bHas ? formatter(b) : "N/A")}");
                return;
            }
            string va = formatter(a);
            string vb = formatter(b);
            if (va != vb) diffs.Add($"{label}: {va} \u2192 {vb}");
        }

        private static void CompareTexture(Material a, Material b, string prop, string label, List<string> diffs)
        {
            bool aHas = a.HasProperty(prop);
            bool bHas = b.HasProperty(prop);
            if (!aHas && !bHas) return;
            var ta = aHas ? a.GetTexture(prop) : null;
            var tb = bHas ? b.GetTexture(prop) : null;
            string na = ta != null ? ta.name : "(\u65E0)";
            string nb = tb != null ? tb.name : "(\u65E0)";
            if (na != nb) diffs.Add($"{label}: {na} \u2192 {nb}");
        }

        // ════════════════════════════════════════════════════════════════
        // 操作
        // ════════════════════════════════════════════════════════════════

        private void OnSwap()
        {
            var tmpMat = _cs.MaterialA.Value;
            var tmpLabel = _cs.LabelA.Value;
            var tmpTex = _cs.PreviewA.Value;

            _cs.LabelA.Value = _cs.LabelB.Value;
            _cs.PreviewA.Value = _cs.PreviewB.Value;
            _cs.MaterialA.Value = _cs.MaterialB.Value;

            _cs.LabelB.Value = tmpLabel;
            _cs.PreviewB.Value = tmpTex;
            _cs.MaterialB.Value = tmpMat;

            ComputeDiff();
            PublishContext();
        }

        private void OnApplyB()
        {
            var matB = _cs.MaterialB.Value;
            if (matB == null) return;

            var sel = _state?.UnitySelection.Value;
            if (sel is not GameObject go) return;

            // 通过 ActionController 统一 Undo 分组 + CommandHistory 记录
            if (_actionController != null)
            {
                var result = _actionController.CompareApply(go, matB);
                if (!result.Success) return;
            }
            else
            {
                // 降级回退：ActionController 未注入
                var renderer = go.GetComponent<Renderer>();
                if (renderer == null) return;

                Undo.RecordObject(renderer, "\u5BF9\u6BD4\u5E94\u7528\u6750\u8D28");
                renderer.sharedMaterial = matB;

                _state.StatusMessage.Value = $"\u5DF2\u5E94\u7528 {matB.name} \u5230 {go.name}";
                ActionEvents.Executed.Publish(new ActionExecutedEvent(
                    "CompareApply", $"\u5DF2\u5C06 {matB.name} \u5E94\u7528\u5230 {go.name}"));
            }

            // 应用后 A 侧更新为新材质
            SetSideA(matB, go.name, "Hierarchy \u5F53\u524D");
        }

        // ════════════════════════════════════════════════════════════════
        // 自动填充 — 从现有状态推断 A/B
        // ════════════════════════════════════════════════════════════════

        private void TryAutoFill()
        {
            // 优先：A 来自 Hierarchy 当前选中物体的材质
            var sel = _state?.UnitySelection.Value;
            if (sel is GameObject go)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    SetSideA(r.sharedMaterial, go.name, "Hierarchy \u9009\u4E2D");
            }

            // B 来自当前选中的资产（如果是材质）
            var asset = _state?.SelectedAsset.Value;
            if (asset?.UnityObject is Material mat)
                SetSideB(mat, asset.DisplayName, "\u8D44\u4EA7\u6D4F\u89C8");
        }

        // ════════════════════════════════════════════════════════════════
        // 预览图请求
        // ════════════════════════════════════════════════════════════════

        private void RequestPreview(Material mat, bool isA)
        {
            if (mat == null) return;

            var preview = AssetPreview.GetAssetPreview(mat);
            if (preview != null)
            {
                if (isA) _cs.PreviewA.Value = preview;
                else _cs.PreviewB.Value = preview;
                return;
            }

            // 先用缩略图，异步等高清
            var mini = AssetPreview.GetMiniThumbnail(mat);
            if (isA) _cs.PreviewA.Value = mini;
            else _cs.PreviewB.Value = mini;

            if (isA) _pollMatA = mat;
            else _pollMatB = mat;
            EditorApplication.update += PollPreview;
        }

        private int _pollSkipCounter;

        private void PollPreview()
        {
            // 节流：非 Compare 模式时跳过轮询，降低空闲开销
            if (_state != null && _state.CurrentViewMode.Value != ViewMode.Compare)
            {
                _pollSkipCounter++;
                if (_pollSkipCounter < 30) return; // 约 0.5 秒检查一次
                _pollSkipCounter = 0;
            }

            bool done = true;

            if (_pollMatA != null)
            {
                var tex = AssetPreview.GetAssetPreview(_pollMatA);
                if (tex != null)
                {
                    _cs.PreviewA.Value = tex;
                    _pollMatA = null;
                }
                else done = false;
            }

            if (_pollMatB != null)
            {
                var tex = AssetPreview.GetAssetPreview(_pollMatB);
                if (tex != null)
                {
                    _cs.PreviewB.Value = tex;
                    _pollMatB = null;
                }
                else done = false;
            }

            if (done)
                EditorApplication.update -= PollPreview;
        }

        // ════════════════════════════════════════════════════════════════
        // 上下文发布 → InspectorPanel
        // ════════════════════════════════════════════════════════════════

        private void PublishContext()
        {
            var a = _cs.MaterialA.Value;
            var b = _cs.MaterialB.Value;

            if (a == null && b == null)
            {
                SelectionEvents.ContextCleared.Publish(new SelectionContextClearedEvent(
                    "Compare", "\u9009\u62E9\u4E24\u4E2A\u6750\u8D28\u5F00\u59CB\u5BF9\u6BD4"));
                return;
            }

            var titleParts = new List<string>();
            if (a != null) titleParts.Add(a.name);
            if (b != null) titleParts.Add(b.name);

            var detail = "";
            if (a != null) detail += $"A: {a.name} ({a.shader.name})";
            if (a != null && b != null) detail += "\n";
            if (b != null) detail += $"B: {b.name} ({b.shader.name})";

            var diff = _cs.DiffSummary.Value;
            if (!string.IsNullOrEmpty(diff))
                detail += "\n\n" + diff;

            SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                "Compare",
                string.Join(" vs ", titleParts),
                $"{_cs.LabelA.Value} \u2194 {_cs.LabelB.Value}",
                detail,
                b != null ? "\u53EF\u70B9\u51FB\u300C\u5E94\u7528 B \u5230\u573A\u666F\u300D\u66FF\u6362\u5F53\u524D\u6750\u8D28" : "\u8BF7\u9009\u62E9\u7B2C\u4E8C\u4E2A\u6750\u8D28\u5B8C\u6210\u5BF9\u6BD4"));
        }
    }
}
