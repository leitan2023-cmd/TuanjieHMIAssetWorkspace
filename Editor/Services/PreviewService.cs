using UnityEditor;
using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    public sealed class PreviewService : IPreviewService
    {
        public Texture2D GetThumbnail(Object asset) => asset == null ? null : AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
        public Texture2D RenderInteractivePreview(Object asset, Vector2 previewSize) => GetThumbnail(asset);
        public void Dispose() { }
    }
}
