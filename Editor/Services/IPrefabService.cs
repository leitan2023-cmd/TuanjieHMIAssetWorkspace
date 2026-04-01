using UnityEngine;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public interface IPrefabService
    {
        GameObject Instantiate(string assetPath, Vector3 position);
        bool CanApplyTo(AssetEntry asset, GameObject target);
        void ApplyAsset(AssetEntry asset, GameObject target);
    }
}
