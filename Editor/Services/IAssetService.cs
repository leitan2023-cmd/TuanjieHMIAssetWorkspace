using System;
using System.Collections.Generic;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public interface IAssetService
    {
        // ── 多根路径管理 ──

        /// <summary>添加一个扫描根路径（如 "Assets/" 或包路径）。重复添加同一路径不会产生副作用。</summary>
        void AddScanRoot(string path);

        /// <summary>清除所有已注册的扫描根路径。</summary>
        void ClearScanRoots();

        /// <summary>获取当前已注册的所有扫描根路径（只读快照）。</summary>
        IReadOnlyList<string> GetScanRoots();

        /// <summary>
        /// 根据当前项目环境自动注册默认扫描根（Assets/ + 已安装的包路径）。
        /// 调用方在 Initialize 时调用一次即可。
        /// </summary>
        void SetupDefaultScanRoots();

        // ── 扫描 ──

        /// <summary>
        /// 扫描所有已注册的根路径，合并去重后返回资产列表。
        /// typeFilters 格式遵循 AssetDatabase.FindAssets 语法，例如 "t:Material"。
        /// </summary>
        List<AssetEntry> ScanAllRoots(string[] typeFilters = null);

        /// <summary>
        /// 扫描指定单一目录（保留原有能力，供需要精确控制路径的场景使用）。
        /// </summary>
        List<AssetEntry> ScanAssets(string rootPath, string[] typeFilters = null);

        // ── 单资产操作 ──

        AssetEntry GetAssetByGuid(string guid);
        string[] GetDependencies(string guid, bool recursive = true);
        void Reimport(string guid);

        // ── 事件 ──

        event Action<string[]> AssetsChanged;
    }
}
