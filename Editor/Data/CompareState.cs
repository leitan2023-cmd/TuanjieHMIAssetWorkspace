using UnityEngine;
using HMI.Workspace.Editor.Core;

namespace HMI.Workspace.Editor.Data
{
    /// <summary>
    /// 对比工作区的响应式状态。
    /// 持有 A / B 两侧材质信息，供 CompareView 绑定。
    ///
    /// 数据来源：
    ///   1. BatchReplace：CurrentMaterial → A，SelectedCandidate.Material → B
    ///   2. AssetGrid 选中材质 → B，Hierarchy 当前材质 → A
    ///   3. 手动拖入 / 选择
    /// </summary>
    public sealed class CompareState
    {
        // ── A 侧（当前 / 原始） ──
        public Observable<Material> MaterialA { get; } = new();
        public Observable<string> LabelA { get; } = new("\u5F53\u524D\u6750\u8D28");
        public Observable<Texture2D> PreviewA { get; } = new();

        // ── B 侧（候选 / 替换） ──
        public Observable<Material> MaterialB { get; } = new();
        public Observable<string> LabelB { get; } = new("\u5019\u9009\u6750\u8D28");
        public Observable<Texture2D> PreviewB { get; } = new();

        // ── 对比结果摘要 ──
        public Observable<string> DiffSummary { get; } = new("");
    }
}
