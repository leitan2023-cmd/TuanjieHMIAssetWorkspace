using System.Collections.Generic;
using UnityEngine;
using HMI.Workspace.Editor.Core;

namespace HMI.Workspace.Editor.Data
{
    /// <summary>
    /// 替换候选项：一条材质 + 其预览缩略图。
    /// </summary>
    public sealed class ReplacementCandidate
    {
        public string Guid { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public Material Material { get; set; }
        public Texture2D Preview { get; set; }
    }

    /// <summary>
    /// 批量替换工作区的完整响应式状态。
    /// </summary>
    public sealed class BatchReplaceState
    {
        /// <summary>目标过滤器：CarBody / Wheels / All / 自定义</summary>
        public Observable<string> TargetFilter { get; } = new("全部");

        /// <summary>当前选中的 Hierarchy 目标对象</summary>
        public Observable<GameObject> CurrentTarget { get; } = new();

        /// <summary>目标对象上当前使用的材质</summary>
        public Observable<Material> CurrentMaterial { get; } = new();

        /// <summary>可供替换的候选材质列表</summary>
        public Observable<List<ReplacementCandidate>> Candidates { get; } = new(new List<ReplacementCandidate>());

        /// <summary>用户选中的候选材质</summary>
        public Observable<ReplacementCandidate> SelectedCandidate { get; } = new();

        /// <summary>受影响的 Renderer 数量</summary>
        public Observable<int> AffectedCount { get; } = new(0);

        /// <summary>替换历史（用于显示最近操作）</summary>
        public Observable<List<string>> History { get; } = new(new List<string>());
    }
}
