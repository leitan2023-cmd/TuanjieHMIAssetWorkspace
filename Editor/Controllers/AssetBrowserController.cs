using System.Linq;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class AssetBrowserController : IController
    {
        // 扫描的根目录。
        // 限定在测试目录而非 "Assets" 根，防止首次打开时扫描整个项目。
        // 需要在 Unity 项目中手动创建此目录并放入资产。
        private const string ScanRoot = "Assets/HMIWorkspaceTest";

        // 只扫描三种基础类型，对应 AssetDatabase.FindAssets 过滤语法（空格分隔 = OR）
        private static readonly string[] SupportedTypeFilters =
        {
            "t:Material",
            "t:Texture2D",
            "t:Prefab",
        };

        private readonly IAssetService _assetService;
        private readonly AssetRegistry _assetRegistry;
        private readonly WorkspaceState _state;

        public AssetBrowserController(IAssetService assetService, AssetRegistry assetRegistry, WorkspaceState state)
        {
            _assetService = assetService;
            _assetRegistry = assetRegistry;
            _state = state;
        }

        /// <summary>
        /// 扫描 ScanRoot 目录下的 Material / Texture2D / Prefab 资产并填充注册表。
        /// 若目录不存在，AssetService 返回空列表并在状态栏给出提示。
        /// </summary>
        public void Initialize()
        {
            var assets = _assetService.ScanAssets(ScanRoot, SupportedTypeFilters);
            _assetRegistry.ReplaceAll(assets);

            // 发布初始列表，驱动 CenterPanel ListView 刷新
            AssetEvents.FilteredChanged.Publish(new FilteredAssetsChangedEvent(_assetRegistry.All));

            _state.StatusMessage.Value = _assetRegistry.All.Count > 0
                ? $"Loaded {_assetRegistry.All.Count} assets from {ScanRoot}"
                : $"No assets found. Create folder: {ScanRoot}";
        }

        /// <summary>
        /// 根据搜索关键字过滤资产列表，发布事件驱动 View 刷新。
        /// 当前使用 DisplayName 的大小写不敏感子串匹配。
        /// </summary>
        public void ApplySearch(string query)
        {
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _assetRegistry.All
                : _assetRegistry.All.Where(a =>
                    a.DisplayName != null &&
                    a.DisplayName.ToLowerInvariant().Contains(query.ToLowerInvariant())
                  ).ToList();

            AssetEvents.FilteredChanged.Publish(new FilteredAssetsChangedEvent(filtered.ToList()));
        }

        public void Dispose() { }
    }
}
