using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    public sealed class AssetEntry
    {
        public string Guid { get; set; }
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public AssetKind Kind { get; set; }
        public Object UnityObject { get; set; }

        public override string ToString() => DisplayName ?? Guid ?? base.ToString();
    }
}
