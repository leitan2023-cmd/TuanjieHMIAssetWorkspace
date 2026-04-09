using System.Collections.Generic;

namespace HMI.Workspace.Editor.Services
{
    // ── 依赖健康状态 ──
    public enum PackageHealth
    {
        Missing,          // 未安装
        Installed,        // 已安装，版本满足
        VersionMismatch,  // 已安装，版本不满足最低要求
    }

    // ── 单包检测结果 ──
    public readonly struct PackageCheckResult
    {
        public PackageCheckResult(
            string packageName,
            string displayName,
            string installedVersion,
            string requiredVersion,
            PackageHealth health)
        {
            PackageName = packageName;
            DisplayName = displayName;
            InstalledVersion = installedVersion;
            RequiredVersion = requiredVersion;
            Health = health;
        }

        public string PackageName { get; }
        public string DisplayName { get; }
        /// <summary>实际安装版本，Missing 时为空字符串</summary>
        public string InstalledVersion { get; }
        /// <summary>最低要求版本</summary>
        public string RequiredVersion { get; }
        public PackageHealth Health { get; }
    }

    // ── 四包整体报告 ──
    public readonly struct HMIRPDependencyReport
    {
        public HMIRPDependencyReport(
            PackageCheckResult core,
            PackageCheckResult shaderLibrary,
            PackageCheckResult materialLibrary,
            PackageCheckResult stateRenderSystem)
        {
            Core = core;
            ShaderLibrary = shaderLibrary;
            MaterialLibrary = materialLibrary;
            StateRenderSystem = stateRenderSystem;
        }

        public PackageCheckResult Core { get; }
        public PackageCheckResult ShaderLibrary { get; }
        public PackageCheckResult MaterialLibrary { get; }
        public PackageCheckResult StateRenderSystem { get; }

        /// <summary>四包全部 Installed</summary>
        public bool AllHealthy =>
            Core.Health == PackageHealth.Installed
            && ShaderLibrary.Health == PackageHealth.Installed
            && MaterialLibrary.Health == PackageHealth.Installed
            && StateRenderSystem.Health == PackageHealth.Installed;

        /// <summary>Core 和 ShaderLibrary 已装，材质预览可用</summary>
        public bool CanPreviewMaterials =>
            Core.Health != PackageHealth.Missing
            && ShaderLibrary.Health != PackageHealth.Missing;

        /// <summary>遍历所有四包</summary>
        public IEnumerable<PackageCheckResult> All()
        {
            yield return Core;
            yield return ShaderLibrary;
            yield return MaterialLibrary;
            yield return StateRenderSystem;
        }
    }

    public interface IPackageService
    {
        /// <summary>检测当前渲染管线名称（保留原有能力）</summary>
        string DetectPipeline();

        /// <summary>检测 HMIRP 四包安装状态与版本</summary>
        HMIRPDependencyReport CheckHMIRPPackages();
    }
}
