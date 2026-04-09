using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class AssetBrowserController : IController
    {
        // 扫描的资产类型，对应 AssetDatabase.FindAssets 过滤语法（空格分隔 = OR）
        // 材质库: Material  |  模型库: Prefab, Model  |  特效库: Texture2D, Shader
        private static readonly string[] SupportedTypeFilters =
        {
            "t:Material",
            "t:Texture2D",
            "t:Prefab",
            "t:Shader",
            "t:Model",
        };

        private readonly IAssetService _assetService;
        private readonly AssetRegistry _assetRegistry;
        private readonly WorkspaceState _state;
        private string _searchQuery = string.Empty;
        private string _sidebarFilter = "my-assets";
        private string _categoryFilter = string.Empty;

        // 搜索 debounce（200ms）
        private double _searchDebounceTime;
        private string _pendingSearchQuery;
        private bool _searchDebouncing;

        // 预分配过滤结果列表，避免每次 ToList() 分配新内存
        private readonly List<AssetEntry> _filteredBuffer = new();

        public AssetBrowserController(IAssetService assetService, AssetRegistry assetRegistry, WorkspaceState state)
        {
            _assetService = assetService;
            _assetRegistry = assetRegistry;
            _state = state;
        }

        /// <summary>
        /// 建立默认扫描根（Assets/ + 已安装的 Material Library 包），
        /// 扫描所有根路径下的资产并填充注册表。
        /// </summary>
        public void Initialize()
        {
            using var _t = PerfTrace.Begin("AssetBrowserController.Initialize");
            // 建立扫描根路径（Assets/ + Material Library 包路径，如已安装）
            _assetService.SetupDefaultScanRoots();

            // 扫描所有根路径
            var assets = _assetService.ScanAllRoots(SupportedTypeFilters);
            _assetRegistry.ReplaceAll(assets);

            // 写入资产统计（单次遍历，替代 3 次 LINQ Count）
            var allAssets = _assetRegistry.All;
            _state.TotalAssetCount.Value = allAssets.Count;
            int matCount = 0, modelCount = 0, effectCount = 0;
            for (int i = 0; i < allAssets.Count; i++)
            {
                var cat = allAssets[i].Category;
                if (string.Equals(cat, "\u6750\u8D28\u5E93", System.StringComparison.OrdinalIgnoreCase)) matCount++;
                else if (string.Equals(cat, "\u6A21\u578B\u5E93", System.StringComparison.OrdinalIgnoreCase)) modelCount++;
                else if (string.Equals(cat, "\u7279\u6548\u5E93", System.StringComparison.OrdinalIgnoreCase)) effectCount++;
            }
            _state.MaterialCount.Value = matCount;
            _state.ModelCount.Value = modelCount;
            _state.EffectCount.Value = effectCount;

            // 发布初始列表，驱动 CenterPanel ListView 刷新
            PublishFilteredAssets();

            // 状态栏提示
            var roots = _assetService.GetScanRoots();
            var rootsSummary = string.Join(", ", roots);
            _state.StatusMessage.Value = _assetRegistry.All.Count > 0
                ? $"\u5DF2\u4ECE {roots.Count} \u4E2A\u6E90\u52A0\u8F7D {_assetRegistry.All.Count} \u4E2A\u8D44\u4EA7 ({rootsSummary})"
                : $"\u672A\u627E\u5230\u8D44\u4EA7\uFF0C\u5F53\u524D\u626B\u63CF\u6E90: {rootsSummary}";
        }

        /// <summary>
        /// 根据搜索关键字过滤资产列表，发布事件驱动 View 刷新。
        /// 使用 200ms debounce 防止逐字输入时频繁重建卡片列表。
        /// </summary>
        public void ApplySearch(string query)
        {
            _pendingSearchQuery = query ?? string.Empty;
            _state.SearchKeyword.Value = _pendingSearchQuery;

            // 首次输入或从空变非空时立即执行
            if (string.IsNullOrEmpty(_searchQuery) && !string.IsNullOrEmpty(_pendingSearchQuery))
            {
                CommitSearch();
                return;
            }

            // debounce：延迟 200ms 执行
            _searchDebounceTime = EditorApplication.timeSinceStartup + 0.2;
            if (!_searchDebouncing)
            {
                _searchDebouncing = true;
                EditorApplication.update += PollSearchDebounce;
            }
        }

        private void PollSearchDebounce()
        {
            if (EditorApplication.timeSinceStartup < _searchDebounceTime) return;
            EditorApplication.update -= PollSearchDebounce;
            _searchDebouncing = false;
            CommitSearch();
        }

        private void CommitSearch()
        {
            _searchQuery = _pendingSearchQuery ?? string.Empty;
            PublishFilteredAssets();
        }

        public void SetSidebarFilter(string filterId)
        {
            _sidebarFilter = string.IsNullOrWhiteSpace(filterId) ? "my-assets" : filterId;
            _state.CurrentSidebarTab.Value = FilterIdToLabel(_sidebarFilter);
            PublishFilteredAssets();
        }

        public void SetCategoryFilter(string category)
        {
            _categoryFilter = category ?? string.Empty;
            PublishFilteredAssets();
        }

        public IReadOnlyList<AssetEntry> GetAllAssets() => _assetRegistry.All;

        private void PublishFilteredAssets()
        {
            using var _t = PerfTrace.Begin("AssetBrowserController.PublishFilteredAssets");

            // 使用预分配缓冲区避免每次 ToList() 的堆分配
            _filteredBuffer.Clear();

            var all = _assetRegistry.All;
            bool hasSearch = !string.IsNullOrWhiteSpace(_searchQuery);
            bool hasCategoryFilter = !string.IsNullOrWhiteSpace(_categoryFilter);

            // 单次遍历完成搜索 + 分类过滤，替代多层 LINQ Where 链
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];

                // 搜索过滤
                if (hasSearch)
                {
                    bool match = false;
                    if (!string.IsNullOrEmpty(a.DisplayName) &&
                        a.DisplayName.IndexOf(_searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        match = true;
                    else if (!string.IsNullOrEmpty(a.Category) &&
                             a.Category.IndexOf(_searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        match = true;
                    else if (a.Tags != null)
                    {
                        for (int t = 0; t < a.Tags.Length; t++)
                        {
                            if (!string.IsNullOrEmpty(a.Tags[t]) &&
                                a.Tags[t].IndexOf(_searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                match = true;
                                break;
                            }
                        }
                    }
                    if (!match) continue;
                }

                // 分类过滤
                if (hasCategoryFilter &&
                    !string.Equals(a.Category, _categoryFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // 侧边栏行为过滤（仅对需要子集筛选的模式做 continue）
                switch (_sidebarFilter)
                {
                    case "favorites":
                        if (!a.Favorite) continue;
                        break;
                    case "ai-suggestions":
                        if (a.Kind != AssetKind.Material && a.Kind != AssetKind.Prefab) continue;
                        break;
                }

                _filteredBuffer.Add(a);
            }

            // "recent" 和 "ai-suggestions" 需要排序/截断
            if (_sidebarFilter == "recent")
            {
                _filteredBuffer.Sort((x, y) => string.Compare(y.ModifiedDate, x.ModifiedDate, System.StringComparison.Ordinal));
                if (_filteredBuffer.Count > 12)
                    _filteredBuffer.RemoveRange(12, _filteredBuffer.Count - 12);
            }
            else if (_sidebarFilter == "ai-suggestions" && _filteredBuffer.Count > 12)
            {
                _filteredBuffer.RemoveRange(12, _filteredBuffer.Count - 12);
            }

            _state.FilteredAssetCount.Value = _filteredBuffer.Count;
            AssetEvents.FilteredChanged.Publish(new FilteredAssetsChangedEvent(_filteredBuffer));
        }

        private static string FilterIdToLabel(string filterId)
        {
            return filterId switch
            {
                "favorites" => "\u6536\u85CF",
                "recent" => "\u6700\u8FD1\u4F7F\u7528",
                "ai-suggestions" => "AI \u63A8\u8350",
                _ => "\u6211\u7684\u8D44\u4EA7",
            };
        }

        public void Dispose()
        {
            if (_searchDebouncing)
            {
                EditorApplication.update -= PollSearchDebounce;
                _searchDebouncing = false;
            }
        }
    }
}
