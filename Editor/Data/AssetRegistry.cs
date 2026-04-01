using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    public sealed class AssetRegistry
    {
        private readonly List<AssetEntry> _assets = new();

        public IReadOnlyList<AssetEntry> All => _assets;

        public void ReplaceAll(IEnumerable<AssetEntry> assets)
        {
            _assets.Clear();
            _assets.AddRange(assets);
        }

        public AssetEntry FindByGuid(string guid) => _assets.FirstOrDefault(a => a.Guid == guid);
        public AssetEntry FindByObject(Object obj) => _assets.FirstOrDefault(a => a.UnityObject == obj);
    }
}
