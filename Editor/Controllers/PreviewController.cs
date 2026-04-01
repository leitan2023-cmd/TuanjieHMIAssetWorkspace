using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class PreviewController : IController
    {
        private readonly IPreviewService _previewService;
        private readonly PreviewCache _cache;
        private readonly WorkspaceState _state;

        public PreviewController(IPreviewService previewService, PreviewCache cache, WorkspaceState state)
        {
            _previewService = previewService;
            _cache = cache;
            _state = state;
        }

        public void Initialize()
        {
            _state.SelectedAsset.Changed += OnSelectedAssetChanged;
        }

        private void OnSelectedAssetChanged(AssetEntry oldValue, AssetEntry newValue)
        {
            if (newValue?.UnityObject == null) return;
            if (!_cache.TryGet(newValue.Guid, out var texture))
            {
                texture = _previewService.GetThumbnail(newValue.UnityObject);
                if (texture != null) _cache.Set(newValue.Guid, texture);
            }
            if (texture != null) PreviewEvents.TextureReady.Publish(new PreviewTextureReadyEvent(texture));
        }

        public void Dispose()
        {
            _state.SelectedAsset.Changed -= OnSelectedAssetChanged;
            _previewService.Dispose();
            _cache.Clear();
        }
    }
}
