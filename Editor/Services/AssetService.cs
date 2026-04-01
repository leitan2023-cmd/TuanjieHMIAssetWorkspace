using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public sealed class AssetService : IAssetService
    {
        public event Action<string[]> AssetsChanged;

        // 单次扫描最多加载的资产数量，防止误扫大目录时卡顿
        private const int MaxScanCount = 500;

        /// <summary>
        /// 扫描指定目录下的资产。
        /// typeFilters 格式遵循 AssetDatabase.FindAssets 语法，例如 "t:Material t:Texture2D t:Prefab"。
        /// 若目录不存在则返回空列表，不抛出异常。
        /// </summary>
        public List<AssetEntry> ScanAssets(string rootPath, string[] typeFilters = null)
        {
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
                // 超出安全上限时截断，避免首次扫描大目录引起卡顿
                if (assets.Count >= MaxScanCount) break;

                var path = AssetDatabase.GUIDToAssetPath(guid);

                // 跳过目录条目（FindAssets 有时会返回文件夹 GUID）
                if (AssetDatabase.IsValidFolder(path)) continue;

                var obj = AssetDatabase.LoadMainAssetAtPath(path);
                assets.Add(new AssetEntry
                {
                    Guid = guid,
                    Path = path,
                    DisplayName = obj != null
                        ? obj.name
                        : System.IO.Path.GetFileNameWithoutExtension(path),
                    UnityObject = obj,
                    Kind = GuessKind(obj)
                });
            }

            return assets;
        }

        public AssetEntry GetAssetByGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            return obj == null ? null : new AssetEntry { Guid = guid, Path = path, DisplayName = obj.name, UnityObject = obj, Kind = GuessKind(obj) };
        }

        public string[] GetDependencies(string guid, bool recursive = true)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? Array.Empty<string>() : AssetDatabase.GetDependencies(path, recursive);
        }

        public void Reimport(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// 根据资产对象类型推断 AssetKind 枚举值
        /// </summary>
        private static AssetKind GuessKind(UnityEngine.Object obj)
        {
            return obj switch
            {
                Material => AssetKind.Material,
                Shader => AssetKind.Shader,
                Texture => AssetKind.Texture,
                SceneAsset => AssetKind.Scene,
                GameObject => AssetKind.Prefab,
                _ => AssetKind.Unknown
            };
        }
    }
}
