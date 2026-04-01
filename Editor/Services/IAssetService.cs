using System;
using System.Collections.Generic;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public interface IAssetService
    {
        List<AssetEntry> ScanAssets(string rootPath, string[] typeFilters = null);
        AssetEntry GetAssetByGuid(string guid);
        string[] GetDependencies(string guid, bool recursive = true);
        void Reimport(string guid);
        event Action<string[]> AssetsChanged;
    }
}
