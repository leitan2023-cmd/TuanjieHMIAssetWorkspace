using UnityEditor;
using UnityEngine;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public sealed class PrefabService : IPrefabService
    {
        public GameObject Instantiate(string assetPath, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return null;
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null) instance.transform.position = position;
            return instance;
        }

        public bool CanApplyTo(AssetEntry asset, GameObject target)
        {
            if (asset == null || target == null) return false;
            return asset.Kind == AssetKind.Material && target.GetComponent<Renderer>() != null;
        }

        public void ApplyAsset(AssetEntry asset, GameObject target)
        {
            if (asset?.UnityObject is Material material && target != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }
        }
    }
}
