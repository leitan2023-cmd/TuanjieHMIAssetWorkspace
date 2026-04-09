using UnityEngine;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Data
{
    public sealed class WorkspaceState
    {
        // ═══════════════════════════════════════════════════════════
        // 1. 领域状态 — 资产、场景、管线
        // ═══════════════════════════════════════════════════════════

        public Observable<AssetEntry> SelectedAsset { get; } = new() { DebugLabel = "SelectedAsset" };
        public Observable<Object> UnitySelection { get; } = new() { DebugLabel = "UnitySelection" };
        public Observable<SceneInfo> ActiveScene { get; } = new();
        public Observable<string> PipelineName { get; } = new("\u672A\u77E5");
        public Observable<OperatingMode> Mode { get; } = new(OperatingMode.Preview);

        // ═══════════════════════════════════════════════════════════
        // 2. UI 状态 — 视图、工具、进度、提示
        // ═══════════════════════════════════════════════════════════

        public Observable<ViewMode> CurrentViewMode { get; } = new(ViewMode.Home) { DebugLabel = "CurrentViewMode" };
        public Observable<string> StatusMessage { get; } = new("\u5C31\u7EEA") { DebugLabel = "StatusMessage" };
        public Observable<string> CurrentSidebarTab { get; } = new("\u6211\u7684\u8D44\u4EA7");
        public Observable<string> CurrentRightTab { get; } = new("\u9884\u89C8");
        public Observable<float> ProgressValue { get; } = new(0f);

        // ═══════════════════════════════════════════════════════════
        // 2b. 资产统计 — 供 Dashboard / Sidebar / TopBar 消费
        // ═══════════════════════════════════════════════════════════

        public Observable<int> TotalAssetCount { get; } = new(0);
        public Observable<int> FilteredAssetCount { get; } = new(0);
        public Observable<string> SearchKeyword { get; } = new("");
        /// <summary>对比工作区状态（A/B 材质对）</summary>
        public CompareState Compare { get; } = new();

        public Observable<int> MaterialCount { get; } = new(0);
        public Observable<int> ModelCount { get; } = new(0);
        public Observable<int> EffectCount { get; } = new(0);

        // ═══════════════════════════════════════════════════════════
        // 3. 依赖状态 — HMIRP 四包检测结果
        // ═══════════════════════════════════════════════════════════

        /// <summary>HMIRP Core 渲染管线包状态</summary>
        public Observable<PackageHealth> CoreHealth { get; } = new(PackageHealth.Missing);

        /// <summary>HMIRP Shader Library 包状态</summary>
        public Observable<PackageHealth> ShaderLibraryHealth { get; } = new(PackageHealth.Missing);

        /// <summary>HMIRP Material Library 包状态</summary>
        public Observable<PackageHealth> MaterialLibraryHealth { get; } = new(PackageHealth.Missing);

        /// <summary>HMIRP State Render System 包状态</summary>
        public Observable<PackageHealth> StateRenderHealth { get; } = new(PackageHealth.Missing);

        /// <summary>四包完整报告（含版本号等详细信息）</summary>
        public Observable<HMIRPDependencyReport?> DependencyReport { get; } = new(null);

        /// <summary>
        /// 整体工作区环境是否就绪。
        /// 由 DependencyController 在检测完成后计算写入。
        /// true = 四包全部 Installed，false = 存在缺失或版本不匹配。
        /// </summary>
        public Observable<bool> EnvironmentReady { get; } = new(false);
    }
}
