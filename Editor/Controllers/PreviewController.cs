using UnityEditor;
using UnityEngine;
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

        // 异步预览轮询
        private string _pendingGuid;
        private Object _pendingObject;

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
            // 停止上一次轮询
            StopPolling();

            if (newValue?.UnityObject == null) return;

            if (_cache.TryGet(newValue.Guid, out var texture))
            {
                PreviewEvents.TextureReady.Publish(new PreviewTextureReadyEvent(texture));
                return;
            }

            // 尝试获取完整预览
            var fullPreview = PreviewService.GetResolvedPreviewOrNull(newValue.UnityObject);
            if (fullPreview != null)
            {
                _cache.Set(newValue.Guid, fullPreview);
                PreviewEvents.TextureReady.Publish(new PreviewTextureReadyEvent(fullPreview));
                return;
            }

            // 完整预览还没生成完，先发送兜底缩略图，再异步轮询
            var mini = PreviewService.GetBestThumbnail(newValue.UnityObject);
            if (mini != null)
                PreviewEvents.TextureReady.Publish(new PreviewTextureReadyEvent(mini));

            _pendingGuid = newValue.Guid;
            _pendingObject = newValue.UnityObject;
            EditorApplication.update += PollPreview;
        }

        private void PollPreview()
        {
            if (_pendingObject == null)
            {
                StopPolling();
                return;
            }

            var preview = PreviewService.GetResolvedPreviewOrNull(_pendingObject);
            if (preview != null)
            {
                _cache.Set(_pendingGuid, preview);
                PreviewEvents.TextureReady.Publish(new PreviewTextureReadyEvent(preview));
                StopPolling();
            }
        }

        private void StopPolling()
        {
            _pendingGuid = null;
            _pendingObject = null;
            EditorApplication.update -= PollPreview;
        }

        public void Dispose()
        {
            StopPolling();
            _state.SelectedAsset.Changed -= OnSelectedAssetChanged;
            _previewService.Dispose();
            _cache.Clear();
        }
    }
}
