using UnityEngine;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class DependencyController : IController
    {
        private readonly IPackageService _packageService;
        private readonly WorkspaceState _state;

        public DependencyController(IPackageService packageService, WorkspaceState state)
        {
            _packageService = packageService;
            _state = state;
        }

        public void Initialize()
        {
            // ── 1. 渲染管线检测（保留原有逻辑） ──
            _state.PipelineName.Value = _packageService.DetectPipeline();

            // ── 2. HMIRP 四包依赖检测 ──
            CheckDependencies();

            // ── 3. 根据依赖状态决定运行模式 ──
            _state.Mode.Value = _state.EnvironmentReady.Value
                ? OperatingMode.Full
                : OperatingMode.Preview;
        }

        /// <summary>
        /// 执行四包检测，将结果逐一写入 WorkspaceState。
        /// </summary>
        private void CheckDependencies()
        {
            var report = _packageService.CheckHMIRPPackages();

            // 写入完整报告
            _state.DependencyReport.Value = report;

            // 写入各包健康状态
            _state.CoreHealth.Value            = report.Core.Health;
            _state.ShaderLibraryHealth.Value   = report.ShaderLibrary.Health;
            _state.MaterialLibraryHealth.Value = report.MaterialLibrary.Health;
            _state.StateRenderHealth.Value     = report.StateRenderSystem.Health;

            // 写入整体环境状态
            _state.EnvironmentReady.Value = report.AllHealthy;

            // 日志输出，便于调试
            if (report.AllHealthy)
            {
                Debug.Log("[HMI Workspace] HMIRP \u56DB\u5305\u68C0\u6D4B\u901A\u8FC7\uFF0C\u73AF\u5883\u5C31\u7EEA\u3002");
            }
            else
            {
                foreach (var pkg in report.All())
                {
                    if (pkg.Health == PackageHealth.Missing)
                        Debug.LogWarning($"[HMI Workspace] \u7F3A\u5C11\u4F9D\u8D56: {pkg.DisplayName} ({pkg.PackageName})");
                    else if (pkg.Health == PackageHealth.VersionMismatch)
                        Debug.LogWarning($"[HMI Workspace] \u7248\u672C\u4E0D\u5339\u914D: {pkg.DisplayName} \u5F53\u524D {pkg.InstalledVersion}\uFF0C\u8981\u6C42 \u2265 {pkg.RequiredVersion}");
                }
            }
        }

        public void Dispose() { }
    }
}
