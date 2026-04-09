using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public sealed class AssetService : IAssetService
    {
        public event Action<string[]> AssetsChanged;

        // 单次扫描最多加载的资产数量，防止误扫大目录时卡顿
        private const int MaxScanCount = 2000;

        // Material Library 包名，与 PackageService 中的注册表一致
        private const string MaterialLibraryPackage = "com.tuanjie.hmirp.materiallibrary";

        // ── 扫描根路径集合（有序去重） ──
        private readonly List<string> _scanRoots = new();
        private readonly HashSet<string> _scanRootsSet = new(StringComparer.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════════════════
        // 多根路径管理
        // ═══════════════════════════════════════════════════════════

        public void AddScanRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // 统一去掉末尾斜杠
            path = path.TrimEnd('/', '\\');

            if (_scanRootsSet.Add(path))
                _scanRoots.Add(path);
        }

        public void ClearScanRoots()
        {
            _scanRoots.Clear();
            _scanRootsSet.Clear();
        }

        public IReadOnlyList<string> GetScanRoots() => _scanRoots;

        // ═══════════════════════════════════════════════════════════
        // 自动建立默认扫描根
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 根据当前项目环境自动注册默认扫描根。
        /// 调用方（如 AssetBrowserController）在 Initialize 时调用一次即可。
        /// </summary>
        public void SetupDefaultScanRoots()
        {
            using var _ = PerfTrace.Begin("AssetService.SetupDefaultScanRoots");

            // 1. 项目 Assets 目录 — 始终加入
            AddScanRoot("Assets");

            // 2. HMIRP Material Library 包路径 — 已安装时加入
            var materialLibPath = ResolvePackagePath(MaterialLibraryPackage);
            if (!string.IsNullOrEmpty(materialLibPath))
                AddScanRoot(materialLibPath);
        }

        /// <summary>
        /// 解析已安装包在项目中的 AssetDatabase 路径。
        /// 返回 "Packages/com.xxx.yyy" 格式，该路径可直接用于 AssetDatabase.FindAssets。
        /// 如果包未安装，返回 null。
        /// </summary>
        private static string ResolvePackagePath(string packageName)
        {
            // Unity 对 Packages 下的包使用 "Packages/包名" 作为虚拟路径
            var packageAssetPath = $"Packages/{packageName}";

            // 验证包路径是否有效（包已安装且 AssetDatabase 可见）
            // AssetDatabase.IsValidFolder 对 Packages/ 下的已安装包返回 true
            if (AssetDatabase.IsValidFolder(packageAssetPath))
                return packageAssetPath;

            // 回退：检查 manifest.json 中 file: 引用的本地包
            // file: 引用的包也通过 Packages/包名 访问
            var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var json = File.ReadAllText(manifestPath);
                // 简单检查 manifest 中是否包含该包名
                if (json.Contains($"\"{packageName}\""))
                {
                    // 即使是 file: 引用，Unity 也会映射为 Packages/包名
                    // 再次验证以确保路径有效
                    if (AssetDatabase.IsValidFolder(packageAssetPath))
                        return packageAssetPath;
                }
            }
            catch
            {
                // manifest 读取失败不影响主流程
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════
        // 扫描
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 扫描所有已注册的根路径，合并去重后返回资产列表。
        /// GUID 作为去重键，确保同一资产不会重复出现。
        /// </summary>
        public List<AssetEntry> ScanAllRoots(string[] typeFilters = null)
        {
            using var _t = PerfTrace.Begin("AssetService.ScanAllRoots");
            var mergedAssets = new List<AssetEntry>();
            var seenGuids = new HashSet<string>();

            foreach (var root in _scanRoots)
            {
                if (mergedAssets.Count >= MaxScanCount) break;

                var assets = ScanAssets(root, typeFilters);
                foreach (var asset in assets)
                {
                    if (mergedAssets.Count >= MaxScanCount) break;
                    if (seenGuids.Add(asset.Guid))
                        mergedAssets.Add(asset);
                }
            }

            return mergedAssets;
        }

        /// <summary>
        /// 扫描指定目录下的资产（保留原有能力）。
        /// typeFilters 格式遵循 AssetDatabase.FindAssets 语法，例如 "t:Material t:Texture2D t:Prefab"。
        /// 若目录不存在则返回空列表，不抛出异常。
        /// </summary>
        public List<AssetEntry> ScanAssets(string rootPath, string[] typeFilters = null)
        {
            using var _t = PerfTrace.Begin($"AssetService.ScanAssets({rootPath})");
            var assets = new List<AssetEntry>();

            // 目录不存在时快速返回，避免 FindAssets 扫描根目录
            if (!AssetDatabase.IsValidFolder(rootPath))
                return assets;

            var filter = (typeFilters == null || typeFilters.Length == 0)
                ? string.Empty
                : string.Join(" ", typeFilters);

            var guids = AssetDatabase.FindAssets(filter, new[] { rootPath });

            foreach (var guid in guids)
            {
                // 超出安全上限时截断
                if (assets.Count >= MaxScanCount) break;

                var path = AssetDatabase.GUIDToAssetPath(guid);

                // 跳过目录条目（FindAssets 有时会返回文件夹 GUID）
                if (AssetDatabase.IsValidFolder(path)) continue;

                var obj = AssetDatabase.LoadMainAssetAtPath(path);
                var kind = GuessKind(obj);

                // 从文件系统读取大小和修改时间
                var fileInfo = new System.IO.FileInfo(path);
                var fileSize = fileInfo.Exists ? FormatFileSize(fileInfo.Length) : "";
                var modifiedDate = fileInfo.Exists
                    ? fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    : "";

                // 材质额外信息
                var shaderName = (obj is Material mat) ? mat.shader?.name ?? "" : "";

                // 来源标识
                var sourceLabel = path.StartsWith("Packages/")
                    ? GuessPackageDisplayName(path)
                    : "Assets";

                assets.Add(new AssetEntry
                {
                    Guid = guid,
                    Path = path,
                    DisplayName = obj != null
                        ? obj.name
                        : System.IO.Path.GetFileNameWithoutExtension(path),
                    UnityObject = obj,
                    Kind = kind,
                    Tags = obj != null ? AssetDatabase.GetLabels(obj) : Array.Empty<string>(),
                    Favorite = false,
                    Status = "ready",
                    FileSize = fileSize,
                    ModifiedDate = modifiedDate,
                    Category = KindToCategory(kind),
                    Labels = obj != null ? AssetDatabase.GetLabels(obj) : Array.Empty<string>(),
                    SourceLabel = sourceLabel,
                    ShaderName = shaderName,
                });
            }

            return assets;
        }

        // ═══════════════════════════════════════════════════════════
        // 单资产操作（保持不变）
        // ═══════════════════════════════════════════════════════════

        public AssetEntry GetAssetByGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            return obj == null
                ? null
                : new AssetEntry
                {
                    Guid = guid,
                    Path = path,
                    DisplayName = obj.name,
                    UnityObject = obj,
                    Kind = GuessKind(obj),
                };
        }

        public string[] GetDependencies(string guid, bool recursive = true)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? Array.Empty<string>()
                : AssetDatabase.GetDependencies(path, recursive);
        }

        public void Reimport(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // ═══════════════════════════════════════════════════════════
        // 工具方法
        // ═══════════════════════════════════════════════════════════

        /// <summary>从包路径推断简短显示名。</summary>
        private static string GuessPackageDisplayName(string path)
        {
            // "Packages/com.tuanjie.hmirp.materiallibrary/..." → "Material Library"
            if (path.Contains("materiallibrary")) return "Material Library";
            if (path.Contains("shaderlibrary")) return "Shader Library";
            if (path.Contains("staterendersystem")) return "State Render";
            if (path.Contains("render-pipelines")) return "HMIRP Core";
            return "Package";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private static string KindToCategory(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "\u6750\u8D28\u5E93",
                AssetKind.Prefab   => "\u6A21\u578B\u5E93",
                AssetKind.Model    => "\u6A21\u578B\u5E93",
                AssetKind.Texture  => "\u7279\u6548\u5E93",
                AssetKind.Shader   => "\u7279\u6548\u5E93",
                AssetKind.Fx       => "\u7279\u6548\u5E93",
                AssetKind.Scene    => "\u6A21\u578B\u5E93",
                _                  => "\u5176\u4ED6",
            };
        }

        /// <summary>
        /// 根据资产对象类型推断 AssetKind 枚举值
        /// </summary>
        private static AssetKind GuessKind(UnityEngine.Object obj)
        {
            return obj switch
            {
                Material  => AssetKind.Material,
                Shader    => AssetKind.Shader,
                Texture   => AssetKind.Texture,
                SceneAsset => AssetKind.Scene,
                GameObject => AssetKind.Prefab,
                _          => AssetKind.Unknown,
            };
        }
    }
}
