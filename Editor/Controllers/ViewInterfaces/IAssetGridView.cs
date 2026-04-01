using UnityEngine;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Controllers.ViewInterfaces
{
    public interface IAssetGridView
    {
        void ScrollToAsset(string guid);
        void RefreshVisibleItems();
        void ShowContextMenu(AssetEntry entry, Vector2 screenPosition);
    }
}
