using UnityEngine;

namespace HMI.Workspace.Editor.Services
{
    public interface IPreviewService
    {
        Texture2D GetThumbnail(Object asset);
        Texture2D RenderInteractivePreview(Object asset, Vector2 previewSize);
        void Dispose();
    }
}
