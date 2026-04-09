using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    public sealed class AssetRegistry
    {
        private readonly List<AssetEntry> _assets = new();

        // O(1) 查找索引
        private readonly Dictionary<string, AssetEntry> _guidIndex = new();
        private readonly Dictionary<Object, AssetEntry> _objectIndex = new();

        public IReadOnlyList<AssetEntry> All => _assets;

        public void ReplaceAll(IEnumerable<AssetEntry> assets)
        {
            _assets.Clear();
            _guidIndex.Clear();
            _objectIndex.Clear();

            foreach (var entry in assets)
            {
                _assets.Add(entry);
                if (!string.IsNullOrEmpty(entry.Guid))
                    _guidIndex[entry.Guid] = entry;
                if (entry.UnityObject != null)
                    _objectIndex[entry.UnityObject] = entry;
            }
        }

        public AssetEntry FindByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return _guidIndex.TryGetValue(guid, out var entry) ? entry : null;
        }

        public AssetEntry FindByObject(Object obj)
        {
            if (obj == null) return null;
            return _objectIndex.TryGetValue(obj, out var entry) ? entry : null;
        }
    }
}
