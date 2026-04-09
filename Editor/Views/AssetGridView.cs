using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// CenterPanel 资产卡片网格视图（implements IAssetGridView）。
    /// 使用 ScrollView + flex-wrap 实现卡片网格，支持：
    ///   - 分类标签页（全部 / 材质库 / 模型库 / 特效库）
    ///   - 缩放滑条控制图标大小（大图卡片 ↔ 文字列表）
    ///   - "全部"模式下按分类分组并用分割线隔开
    ///   - Shader 不作为首页主入口资产，仅在技术详情中展示
    /// </summary>
    public sealed class AssetGridView : IAssetGridView
    {
        private readonly VisualElement _root;
        private List<AssetEntry> _items = new();
        private List<AssetEntry> _filteredByCategory = new();
        private SelectionController _selectionController;
        private WorkspaceState _state;

        private VisualElement _cardContainer;
        private Label _countLabel;
        private string _activeCategory = "all";
        private readonly List<Button> _categoryButtons = new();
        private readonly Dictionary<string, VisualElement> _cardMap = new();

        // 缩放
        private Slider _sizeSlider;
        private float _cardScale = 1f;
        private const float MinCardWidth   = 56f;
        private const float MaxCardWidth   = 260f;
        private const float ListThreshold  = 0.18f;
        private const float ThumbHideThreshold = 0.32f;

        // 分类显示顺序
        private static readonly string[] CategoryOrder = { "\u6750\u8D28\u5E93", "\u6A21\u578B\u5E93", "\u7279\u6548\u5E93", "\u5176\u4ED6" };

        // 异步缩略图
        private readonly HashSet<string> _pendingPreviews = new();
        private bool _pollingPreviews;
        private int _pollFrameCounter;

        // ── 卡片对象池：避免每次 RebuildCards 全量销毁/重建 VisualElement ──
        private readonly Dictionary<string, VisualElement> _cardPool = new();
        private bool _poolIsListMode;

        // ── 分帧创建：首次构建时，前 N 张立即创建，其余分帧创建 ──
        private const int ImmediateCardLimit = 16;
        private const int DeferredBatchSize = 15;
        private List<(AssetEntry entry, VisualElement section)> _deferredItems;
        private int _deferredIndex;
        private bool _deferredBuilding;
        private int _immediateCreated;

        // ── 预览状态追踪：记录已成功加载高清预览的 GUID，避免重复轮询 ──
        private readonly HashSet<string> _previewResolved = new();

        public AssetGridView(VisualElement root)
        {
            _root = root;
        }

        public void Bind(WorkspaceState state, SelectionController selectionController,
            AssetBrowserController assetBrowserController = null)
        {
            _state = state;
            _selectionController = selectionController;
            if (_root == null) return;

            // 扩大 Unity 预览纹理缓存（默认较小，大量资产时容易淘汰）
            AssetPreview.SetPreviewTextureCacheSize(256);

            _root.Clear();

            // ── 顶部工具栏：行为筛选标签 + 缩放滑条 + 计数 ──
            var toolbar = new VisualElement();
            toolbar.AddToClassList("grid-toolbar");

            var tabRow = new VisualElement();
            tabRow.AddToClassList("grid-tab-row");
            tabRow.Add(CreateBehaviorTab("\u5168\u90E8",     "my-assets",      true,  assetBrowserController));
            tabRow.Add(CreateBehaviorTab("\u6700\u8FD1",     "recent",         false, assetBrowserController));
            tabRow.Add(CreateBehaviorTab("\u6536\u85CF",     "favorites",      false, assetBrowserController));
            tabRow.Add(CreateBehaviorTab("AI \u63A8\u8350",  "ai-suggestions", false, assetBrowserController));
            toolbar.Add(tabRow);

            // 缩放滑条
            var sliderRow = new VisualElement();
            sliderRow.AddToClassList("grid-slider-row");

            var smallIcon = new Label("\u2587");
            smallIcon.AddToClassList("grid-slider-icon");
            smallIcon.style.fontSize = 9;

            _sizeSlider = new Slider(0f, 1f) { value = 0.65f };
            _sizeSlider.AddToClassList("grid-size-slider");
            _sizeSlider.RegisterValueChangedCallback(evt =>
            {
                _cardScale = evt.newValue;
                ApplyScale();
            });
            _cardScale = 0.65f;

            var bigIcon = new Label("\u2587");
            bigIcon.AddToClassList("grid-slider-icon");
            bigIcon.style.fontSize = 16;

            sliderRow.Add(smallIcon);
            sliderRow.Add(_sizeSlider);
            sliderRow.Add(bigIcon);
            toolbar.Add(sliderRow);

            _countLabel = new Label("0 \u4E2A\u8D44\u4EA7");
            _countLabel.AddToClassList("grid-count-label");
            toolbar.Add(_countLabel);

            _root.Add(toolbar);

            // ── 卡片网格容器（ScrollView + flex-wrap）──
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;

            _cardContainer = new VisualElement();
            _cardContainer.AddToClassList("card-grid");
            scroll.Add(_cardContainer);
            _root.Add(scroll);

            AssetEvents.FilteredChanged.Subscribe(OnFilteredChanged);
        }

        // ══════════════════════════════════════════════════════════════
        // 行为筛选标签
        // ══════════════════════════════════════════════════════════════

        private Button CreateBehaviorTab(string label, string filterId, bool active,
            AssetBrowserController controller)
        {
            Button btn = null;
            btn = new Button(() =>
            {
                foreach (var b in _categoryButtons)
                    b.RemoveFromClassList("active");
                btn.AddToClassList("active");
                controller?.SetSidebarFilter(filterId);
                _activeCategory = "all";
            }) { text = label };
            btn.AddToClassList("grid-tab");
            if (active) btn.AddToClassList("active");
            _categoryButtons.Add(btn);
            return btn;
        }

        // ══════════════════════════════════════════════════════════════
        // 数据更新 & 构建
        // ══════════════════════════════════════════════════════════════

        private void OnFilteredChanged(FilteredAssetsChangedEvent evt)
        {
            var newItems = evt.Assets;

            // 快速路径：如果 GUID 列表完全相同，跳过重建
            if (newItems.Count == _items.Count && _cardMap.Count > 0)
            {
                bool same = true;
                for (int i = 0; i < newItems.Count; i++)
                {
                    if (newItems[i].Guid != _items[i].Guid) { same = false; break; }
                }
                if (same)
                {
                    PerfTrace.Count("AssetGridView.OnFilteredChanged:skipped(same)");
                    return;
                }
            }

            _items = new List<AssetEntry>(newItems);
            RebuildCards();
        }

        /// <summary>
        /// 过滤主入口资产：Shader 不作为首页资产卡片展示，
        /// 仅在 InspectorPanel 技术依赖区域中显示。
        /// </summary>
        private static bool IsDisplayableAsset(AssetEntry entry)
        {
            return entry.Kind != AssetKind.Shader;
        }

        private void RebuildCards()
        {
            using var _t = PerfTrace.Begin("AssetGridView.RebuildCards");
            if (_cardContainer == null) return;

            // 取消上一轮未完成的分帧构建
            CancelDeferredBuild();

            _cardContainer.Clear();
            _cardMap.Clear();
            _cardChildCache.Clear();
            _pendingPreviews.Clear();

            // 过滤：排除 Shader + 按分类（复用列表避免 GC）
            _filteredByCategory.Clear();
            foreach (var item in _items)
            {
                if (!IsDisplayableAsset(item)) continue;
                if (_activeCategory == "all" ||
                    string.Equals(item.Category, _activeCategory, System.StringComparison.OrdinalIgnoreCase))
                {
                    _filteredByCategory.Add(item);
                }
            }

            if (_countLabel != null)
                _countLabel.text = $"{_filteredByCategory.Count} \u4E2A\u8D44\u4EA7";

            if (_filteredByCategory.Count == 0)
            {
                BuildEmptyState();
                return;
            }

            bool isListMode = _cardScale < ListThreshold;
            _lastBuildWasListMode = isListMode;

            // 模式切换（网格 ↔ 列表）时清空对象池，因为两种模式的 VisualElement 结构不同
            if (_cardPool.Count > 0 && isListMode != _poolIsListMode)
            {
                _cardPool.Clear();
                _previewResolved.Clear();
            }
            _poolIsListMode = isListMode;
            _immediateCreated = 0;

            if (_activeCategory == "all")
                BuildGroupedView(isListMode);
            else
                BuildFlatView(_filteredByCategory, isListMode);

            // 启动分帧创建（仅首次构建、池为空时才有 deferred 项）
            StartDeferredBuild();

            EnsureValidSelection();
            StartPreviewPolling();
        }

        /// <summary>
        /// 全部模式：按 材质库 → 模型库 → 特效库 → 其他 分组。
        /// </summary>
        private void BuildGroupedView(bool isListMode)
        {
            var groups = _filteredByCategory
                .GroupBy(a => a.Category ?? "\u5176\u4ED6")
                .OrderBy(g => System.Array.IndexOf(CategoryOrder, g.Key) is var idx && idx >= 0 ? idx : 999)
                .ToList();

            bool first = true;
            foreach (var group in groups)
            {
                if (!first)
                {
                    var divider = new VisualElement();
                    divider.AddToClassList("grid-category-divider");
                    _cardContainer.Add(divider);
                }
                first = false;

                var headerRow = new VisualElement();
                headerRow.AddToClassList("grid-category-header");

                var catTitle = new Label(group.Key);
                catTitle.AddToClassList("grid-category-title");

                var catCount = new Label($"{group.Count()}");
                catCount.AddToClassList("grid-category-count");

                headerRow.Add(catTitle);
                headerRow.Add(catCount);
                _cardContainer.Add(headerRow);

                var sectionGrid = new VisualElement();
                sectionGrid.AddToClassList(isListMode ? "card-list-section" : "card-grid-section");

                foreach (var entry in group)
                    AddCardToSection(entry, sectionGrid, isListMode);

                _cardContainer.Add(sectionGrid);
            }
        }

        private void BuildFlatView(List<AssetEntry> entries, bool isListMode)
        {
            var grid = new VisualElement();
            grid.AddToClassList(isListMode ? "card-list-section" : "card-grid-section");

            foreach (var entry in entries)
                AddCardToSection(entry, grid, isListMode);

            _cardContainer.Add(grid);
        }

        // ══════════════════════════════════════════════════════════════
        // 卡片对象池 + 分帧创建
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 从池中取出已有卡片（re-attach），或新建卡片并加入池。
        /// 对象池按 GUID 缓存，同一资产的卡片只创建一次。
        /// </summary>
        private VisualElement GetOrCreateCard(AssetEntry entry, bool isListMode)
        {
            if (_cardPool.TryGetValue(entry.Guid, out var card))
            {
                card.RemoveFromHierarchy();
                return card;
            }
            card = isListMode ? CreateListRow(entry) : CreateCard(entry);
            _cardPool[entry.Guid] = card;
            return card;
        }

        /// <summary>
        /// 将卡片添加到 section 容器。
        /// 池中已有的卡片直接 re-attach（~0.1ms）；
        /// 需要新建的卡片在 ImmediateCardLimit 内立即创建，超出的延迟到下一帧。
        /// </summary>
        private void AddCardToSection(AssetEntry entry, VisualElement section, bool isListMode)
        {
            if (_cardPool.ContainsKey(entry.Guid))
            {
                // 池命中：re-attach（极快）
                var card = GetOrCreateCard(entry, isListMode);
                section.Add(card);
                _cardMap[entry.Guid] = card;

                // 池复用时：若高清预览尚未加载，重新加入轮询队列
                if (!_previewResolved.Contains(entry.Guid) && entry.UnityObject != null)
                    _pendingPreviews.Add(entry.Guid);
            }
            else if (_immediateCreated < ImmediateCardLimit)
            {
                // 本帧创建预算内
                _immediateCreated++;
                var card = GetOrCreateCard(entry, isListMode);
                section.Add(card);
                _cardMap[entry.Guid] = card;
            }
            else
            {
                // 超出本帧预算，推迟到后续帧
                _deferredItems ??= new List<(AssetEntry, VisualElement)>(64);
                _deferredItems.Add((entry, section));
            }
        }

        private void CancelDeferredBuild()
        {
            if (_deferredBuilding)
            {
                EditorApplication.update -= ProcessDeferredBuild;
                _deferredBuilding = false;
            }
            _deferredItems = null;
            _deferredIndex = 0;
        }

        private void StartDeferredBuild()
        {
            if (_deferredItems == null || _deferredItems.Count == 0 || _deferredBuilding) return;
            _deferredIndex = 0;
            _deferredBuilding = true;
            EditorApplication.update += ProcessDeferredBuild;
        }

        private void ProcessDeferredBuild()
        {
            if (_deferredItems == null || _deferredIndex >= _deferredItems.Count)
            {
                CancelDeferredBuild();
                return;
            }

            bool isListMode = _poolIsListMode;
            int batchEnd = System.Math.Min(_deferredIndex + DeferredBatchSize, _deferredItems.Count);
            for (int i = _deferredIndex; i < batchEnd; i++)
            {
                var (entry, section) = _deferredItems[i];
                var card = GetOrCreateCard(entry, isListMode);
                section.Add(card);
                _cardMap[entry.Guid] = card;
            }
            _deferredIndex = batchEnd;

            // 每批次结束后确保预览轮询在运行（新建卡片会添加 _pendingPreviews）
            StartPreviewPolling();

            if (_deferredIndex >= _deferredItems.Count)
            {
                PerfTrace.Count("AssetGridView.DeferredBuild:completed");
                CancelDeferredBuild();
                // 分帧创建完成后重新检查选中状态
                EnsureValidSelection();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 缩放
        // ══════════════════════════════════════════════════════════════

        // 缩放时避免 Q() 查询的子元素引用缓存
        private readonly Dictionary<string, (VisualElement thumbArea, Label nameLabel, VisualElement sourceTag, VisualElement shaderRow)> _cardChildCache = new();
        private bool _lastBuildWasListMode;

        private void ApplyScale()
        {
            using var _t = PerfTrace.Begin("AssetGridView.ApplyScale");
            bool isListMode = _cardScale < ListThreshold;

            // 模式切换（网格 ↔ 列表）时必须重建
            if (isListMode != _lastBuildWasListMode && _cardMap.Count > 0)
            {
                RebuildCards();
                return;
            }

            if (!isListMode)
            {
                float width = Mathf.Lerp(MinCardWidth, MaxCardWidth, _cardScale);
                float thumbH = Mathf.Lerp(40f, 160f, _cardScale);
                bool showThumb = _cardScale >= ThumbHideThreshold;
                int fontSize = _cardScale > 0.5f ? 11 : 9;
                var showSource = _cardScale > 0.45f ? DisplayStyle.Flex : DisplayStyle.None;
                var showShader = _cardScale > 0.55f ? DisplayStyle.Flex : DisplayStyle.None;
                var thumbDisplay = showThumb ? DisplayStyle.Flex : DisplayStyle.None;

                foreach (var kv in _cardMap)
                {
                    var card = kv.Value;
                    card.style.width = width;

                    // 使用缓存避免每帧 Q() 查询
                    if (!_cardChildCache.TryGetValue(kv.Key, out var refs))
                    {
                        refs = (
                            card.Q(className: "card-thumb-area"),
                            card.Q<Label>(className: "card-name"),
                            card.Q(className: "card-source-tag"),
                            card.Q(className: "card-shader-row")
                        );
                        _cardChildCache[kv.Key] = refs;
                    }

                    if (refs.thumbArea != null)
                    {
                        refs.thumbArea.style.height = showThumb ? thumbH : 0;
                        refs.thumbArea.style.display = thumbDisplay;
                    }
                    if (refs.nameLabel != null)
                        refs.nameLabel.style.fontSize = fontSize;
                    if (refs.sourceTag != null)
                        refs.sourceTag.style.display = showSource;
                    if (refs.shaderRow != null)
                        refs.shaderRow.style.display = showShader;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 卡片模式 — 增强信息层级
        // ══════════════════════════════════════════════════════════════

        private VisualElement CreateCard(AssetEntry entry)
        {
            float width = Mathf.Lerp(MinCardWidth, MaxCardWidth, _cardScale);
            float thumbH = Mathf.Lerp(40f, 160f, _cardScale);
            bool showThumb = _cardScale >= ThumbHideThreshold;
            bool showDetail = _cardScale > 0.45f;
            bool showShader = _cardScale > 0.55f;

            var card = new VisualElement();
            card.AddToClassList("asset-card");
            card.userData = entry;
            card.style.width = width;

            card.RegisterCallback<ClickEvent>(_ =>
            {
                SetSelectedCard(entry);
                _selectionController?.OnUserSelectAsset(entry);
            });

            card.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                SetSelectedCard(entry);
                _selectionController?.OnUserSelectAsset(entry);
                ShowContextMenu(entry, evt.mousePosition);
            });

            // ── 缩略图区域 ──
            {
                var thumbArea = new VisualElement();
                thumbArea.AddToClassList("card-thumb-area");
                thumbArea.style.height = showThumb ? thumbH : 0;
                thumbArea.style.display = showThumb ? DisplayStyle.Flex : DisplayStyle.None;

                // 直接在 thumbArea 上设置 backgroundImage（绕过 Image 元素尺寸计算问题）
                if (entry.UnityObject != null)
                {
                    var preview = PreviewService.GetBestThumbnail(entry.UnityObject);
                    if (preview != null)
                    {
                        SetThumbBg(thumbArea, preview);
                        _previewResolved.Add(entry.Guid);
                    }
                    if (!_previewResolved.Contains(entry.Guid))
                    {
                        _pendingPreviews.Add(entry.Guid);
                    }
                }

                // 收藏按钮（左上角）
                thumbArea.Add(CreateFavButton(entry));

                // 类型徽章（右下角）
                var typeBadge = new Label(KindToLabel(entry.Kind));
                typeBadge.AddToClassList("card-type-badge");
                typeBadge.AddToClassList(KindToBadgeClass(entry.Kind));
                thumbArea.Add(typeBadge);

                // 来源标签（左下角，仅包来源显示）
                if (!string.IsNullOrEmpty(entry.SourceLabel) && entry.SourceLabel != "Assets" && showDetail)
                {
                    var sourceTag = new Label(entry.SourceLabel);
                    sourceTag.AddToClassList("card-source-tag");
                    thumbArea.Add(sourceTag);
                }

                card.Add(thumbArea);
            }

            // ── 信息区域 ──
            var infoArea = new VisualElement();
            infoArea.AddToClassList("card-info");

            var nameLabel = new Label(entry.DisplayName);
            nameLabel.AddToClassList("card-name");
            nameLabel.style.fontSize = showDetail ? 11 : 9;
            infoArea.Add(nameLabel);

            // 材质卡片：显示 Shader 名称作为技术标注
            if (entry.Kind == AssetKind.Material && !string.IsNullOrEmpty(entry.ShaderName) && showShader)
            {
                var shaderRow = new VisualElement();
                shaderRow.AddToClassList("card-shader-row");

                var shaderLabel = new Label(SimplifyShaderName(entry.ShaderName));
                shaderLabel.AddToClassList("card-shader-name");
                shaderRow.Add(shaderLabel);

                infoArea.Add(shaderRow);
            }

            card.Add(infoArea);
            return card;
        }

        // ══════════════════════════════════════════════════════════════
        // 列表模式
        // ══════════════════════════════════════════════════════════════

        private VisualElement CreateListRow(AssetEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("asset-list-row");
            row.userData = entry;

            row.RegisterCallback<ClickEvent>(_ =>
            {
                SetSelectedCard(entry);
                _selectionController?.OnUserSelectAsset(entry);
            });

            row.RegisterCallback<ContextClickEvent>(evt =>
            {
                evt.StopPropagation();
                SetSelectedCard(entry);
                _selectionController?.OnUserSelectAsset(entry);
                ShowContextMenu(entry, evt.mousePosition);
            });

            var miniThumb = new Image();
            miniThumb.AddToClassList("list-row-icon");
            miniThumb.scaleMode = ScaleMode.ScaleAndCrop;
            miniThumb.image = entry.UnityObject != null
                ? PreviewService.GetBestThumbnail(entry.UnityObject)
                : null;
            row.Add(miniThumb);

            var nameLabel = new Label(entry.DisplayName);
            nameLabel.AddToClassList("list-row-name");
            row.Add(nameLabel);

            var typeLabel = new Label(KindToLabel(entry.Kind));
            typeLabel.AddToClassList("list-row-type");
            row.Add(typeLabel);

            // 来源指示（仅包来源）
            if (!string.IsNullOrEmpty(entry.SourceLabel) && entry.SourceLabel != "Assets")
            {
                var srcLabel = new Label(entry.SourceLabel);
                srcLabel.AddToClassList("list-row-source");
                row.Add(srcLabel);
            }

            row.Add(CreateFavButton(entry));

            return row;
        }

        // ══════════════════════════════════════════════════════════════
        // 收藏按钮
        // ══════════════════════════════════════════════════════════════

        private Button CreateFavButton(AssetEntry entry)
        {
            var btn = new Button { text = entry.Favorite ? "\u2605" : "\u2606" };
            btn.AddToClassList("card-fav-btn");
            if (entry.Favorite) btn.AddToClassList("fav-active");

            btn.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                entry.Favorite = !entry.Favorite;
                btn.text = entry.Favorite ? "\u2605" : "\u2606";
                if (entry.Favorite) btn.AddToClassList("fav-active");
                else btn.RemoveFromClassList("fav-active");
            });

            return btn;
        }

        // ══════════════════════════════════════════════════════════════
        // 选中状态
        // ══════════════════════════════════════════════════════════════

        private void SetSelectedCard(AssetEntry entry)
        {
            foreach (var kv in _cardMap)
                kv.Value.RemoveFromClassList("selected");

            if (entry != null && _cardMap.TryGetValue(entry.Guid, out var card))
                card.AddToClassList("selected");

            if (entry != null)
            {
                var subtitle = $"{KindToLabel(entry.Kind)}  \u2022  {entry.Category ?? "\u672A\u5206\u7C7B"}";
                if (!string.IsNullOrEmpty(entry.SourceLabel) && entry.SourceLabel != "Assets")
                    subtitle += $"  \u2022  {entry.SourceLabel}";

                var detail = entry.Path ?? "";
                if (entry.Kind == AssetKind.Material && !string.IsNullOrEmpty(entry.ShaderName))
                    detail += $"\nShader: {entry.ShaderName}";
                if (!string.IsNullOrEmpty(entry.FileSize))
                    detail += $"\n\u5927\u5C0F: {entry.FileSize}";

                SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                    "AssetBrowser",
                    entry.DisplayName,
                    subtitle,
                    detail,
                    entry.Kind == AssetKind.Material
                        ? "\u5B8C\u6210 Step 1 \u540E\u53EF\u5E94\u7528\u5230\u573A\u666F"
                        : ""));
            }
        }

        private void EnsureValidSelection()
        {
            if (_filteredByCategory.Count == 0) return;

            var current = _state?.SelectedAsset.Value;
            if (current == null) return;

            foreach (var item in _filteredByCategory)
            {
                if (item.Guid == current.Guid)
                {
                    SetSelectedCard(item);
                    return;
                }
            }
        }


        /// <summary>
        /// 在 VisualElement 上设置 backgroundImage。thumbArea 自身有固定高度和宽度，
        /// 不受 Image 元素内部尺寸计算的干扰，可靠渲染纹理。
        /// </summary>
        private static void SetThumbBg(VisualElement el, Texture2D tex)
        {
            el.style.backgroundImage = new StyleBackground(tex);
        }

        // ══════════════════════════════════════════════════════════════
        // 异步缩略图轮询 — 节流处理，避免频繁闪烁
        // ══════════════════════════════════════════════════════════════

        private void StartPreviewPolling()
        {
            if (_pendingPreviews.Count == 0 || _pollingPreviews) return;
            _pollingPreviews = true;
            _pollFrameCounter = 0;
            EditorApplication.update += PollPreviews;
        }

        // 轮询用复用列表，避免每帧 GC 分配
        private readonly List<string> _pollResolvedBuffer = new();
        private readonly List<string> _pollSnapshotBuffer = new();

        private void PollPreviews()
        {
            if (_pendingPreviews.Count == 0)
            {
                _pollingPreviews = false;
                EditorApplication.update -= PollPreviews;
                return;
            }

            // 节流：每 2 帧检查一次
            _pollFrameCounter++;
            if (_pollFrameCounter % 2 != 0) return;

            // 快照 pending 列表（避免迭代期间修改 + 避免 HashSet 饥饿问题）
            _pollSnapshotBuffer.Clear();
            _pollSnapshotBuffer.AddRange(_pendingPreviews);

            _pollResolvedBuffer.Clear();

            for (int i = 0; i < _pollSnapshotBuffer.Count; i++)
            {
                var guid = _pollSnapshotBuffer[i];

                VisualElement card;
                if (!_cardMap.TryGetValue(guid, out card))
                {
                    if (!_cardPool.TryGetValue(guid, out card))
                    {
                        _pollResolvedBuffer.Add(guid);
                        continue;
                    }
                }

                var entry = card.userData as AssetEntry;
                if (entry?.UnityObject == null)
                {
                    _pollResolvedBuffer.Add(guid);
                    continue;
                }

                if (entry.UnityObject is Material || entry.UnityObject is GameObject)
                {
                    _pollResolvedBuffer.Add(guid);
                    continue;
                }

                var preview = PreviewService.GetResolvedPreviewOrNull(entry.UnityObject);
                if (preview != null)
                {
                    // 直接在 card-thumb-area 上更新 backgroundImage
                    var thumbArea = card.Q(className: "card-thumb-area");
                    if (thumbArea != null)
                        SetThumbBg(thumbArea, preview);
                    _previewResolved.Add(guid);
                    _pollResolvedBuffer.Add(guid);
                }
                else if (entry.UnityObject is Material)
                {
                    var thumbArea = card.Q(className: "card-thumb-area");
                    if (thumbArea != null)
                        SetThumbBg(thumbArea, PreviewService.GetBestThumbnail(entry.UnityObject));
                    _pollResolvedBuffer.Add(guid);
                }
            }

            foreach (var guid in _pollResolvedBuffer)
                _pendingPreviews.Remove(guid);
        }

        // ══════════════════════════════════════════════════════════════
        // 空状态
        // ══════════════════════════════════════════════════════════════

        private void BuildEmptyState()
        {
            var empty = new VisualElement();
            empty.AddToClassList("empty-state");

            var icon = new Label("\u25A1");
            icon.AddToClassList("empty-state-icon");
            empty.Add(icon);

            // 根据当前分类给出精确提示
            string title;
            string desc;

            if (_activeCategory != "all")
            {
                title = $"\u300C{_activeCategory}\u300D\u4E0B\u6682\u65E0\u8D44\u4EA7";
                desc = "\u5207\u6362\u5230\u300C\u5168\u90E8\u300D\u67E5\u770B\u6240\u6709\u5DF2\u52A0\u8F7D\u7684\u8D44\u4EA7\uFF0C\u6216\u5B89\u88C5 HMIRP Material Library \u83B7\u53D6\u66F4\u591A\u6750\u8D28\u3002";
            }
            else if (_items.Count > 0 && _filteredByCategory.Count == 0)
            {
                // 有数据但全被过滤掉了（例如全是 Shader）
                title = "\u5F53\u524D\u7B5B\u9009\u65E0\u7ED3\u679C";
                desc = "\u5DF2\u52A0\u8F7D\u7684\u8D44\u4EA7\u4E0D\u5339\u914D\u5F53\u524D\u7B5B\u9009\u6761\u4EF6\uFF0C\u8BF7\u5C1D\u8BD5\u5207\u6362\u5206\u7C7B\u6216\u6E05\u9664\u641C\u7D22\u3002";
            }
            else
            {
                title = "\u5C1A\u65E0\u8D44\u4EA7";
                desc = "\u5C06\u8D44\u4EA7\u6587\u4EF6\u653E\u5165 Assets \u76EE\u5F55\uFF0C\u6216\u5B89\u88C5 HMIRP Material Library \u5305\u3002\n\u4E5F\u53EF\u4EE5\u4F7F\u7528\u8F66\u8F86\u914D\u7F6E\u5DE5\u4F5C\u533A\u5BFC\u5165 FBX \u6A21\u578B\u3002";
            }

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("empty-state-title");
            empty.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("empty-state-text");
            empty.Add(descLabel);

            var btn = new Button(() =>
            {
                _state.CurrentViewMode.Value = ViewMode.VehicleSetup;
            })
            { text = "\u5BFC\u5165\u8F66\u8F86" };
            btn.AddToClassList("empty-state-btn");
            empty.Add(btn);

            _cardContainer.Add(empty);
        }

        // ══════════════════════════════════════════════════════════════
        // 辅助
        // ══════════════════════════════════════════════════════════════

        /// <summary>简化 Shader 名称用于卡片标注。</summary>
        private static string SimplifyShaderName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            // "HMIRP/Car/BodyPaint" → "BodyPaint"
            // "Universal Render Pipeline/Lit" → "Lit"
            var lastSlash = fullName.LastIndexOf('/');
            return lastSlash >= 0 ? fullName.Substring(lastSlash + 1) : fullName;
        }

        private static string KindToLabel(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "\u6750\u8D28",
                AssetKind.Texture  => "\u8D34\u56FE",
                AssetKind.Prefab   => "\u9884\u5236\u4F53",
                AssetKind.Shader   => "\u7740\u8272\u5668",
                AssetKind.Model    => "\u6A21\u578B",
                AssetKind.Scene    => "\u573A\u666F",
                AssetKind.Fx       => "\u7279\u6548",
                _                  => "\u8D44\u4EA7",
            };
        }

        private static string KindToBadgeClass(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "badge-material",
                AssetKind.Texture  => "badge-texture",
                AssetKind.Prefab   => "badge-prefab",
                AssetKind.Model    => "badge-prefab",
                AssetKind.Shader   => "badge-shader",
                AssetKind.Fx       => "badge-shader",
                _                  => "badge-material",
            };
        }

        // ── IAssetGridView ──

        public void ScrollToAsset(string guid)
        {
            if (_cardMap.TryGetValue(guid, out var card))
            {
                var scroll = _root?.Q<ScrollView>();
                scroll?.ScrollTo(card);
            }
        }

        public void RefreshVisibleItems() => RebuildCards();

        public void ShowContextMenu(AssetEntry entry, Vector2 screenPosition)
        {
            if (entry == null) return;

            var menu = new GenericMenu();

            // ① 应用到当前选中对象（仅材质）
            if (entry.Kind == AssetKind.Material)
            {
                var target = Selection.activeGameObject;
                bool canApply = target != null && target.GetComponent<Renderer>() != null;
                if (canApply)
                {
                    menu.AddItem(new GUIContent($"\u5E94\u7528\u5230\u300C{target.name}\u300D"), false, () =>
                    {
                        _selectionController?.OnUserSelectAsset(entry);
                        // 延迟一帧让 SelectedAsset 生效后触发
                        EditorApplication.delayCall += () =>
                        {
                            ActionEvents.StatesChanged.Publish(new ActionStatesChangedEvent());
                        };
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("\u5E94\u7528\u5230\u9009\u4E2D\u5BF9\u8C61\uFF08\u8BF7\u5148\u9009\u4E2D GameObject\uFF09"));
                }

                menu.AddSeparator("");

                // ② 进入对比 — 作为 A 侧
                menu.AddItem(new GUIContent("\u5BF9\u6BD4 \u2192 \u8BBE\u4E3A A \u4FA7\uFF08\u5F53\u524D\uFF09"), false, () =>
                {
                    if (entry.UnityObject is Material mat)
                    {
                        _state.Compare.LabelA.Value = "\u8D44\u4EA7\u6D4F\u89C8";
                        _state.Compare.MaterialA.Value = mat;
                        _state.CurrentViewMode.Value = ViewMode.Compare;
                    }
                });

                // ③ 进入对比 — 作为 B 侧
                menu.AddItem(new GUIContent("\u5BF9\u6BD4 \u2192 \u8BBE\u4E3A B \u4FA7\uFF08\u5019\u9009\uFF09"), false, () =>
                {
                    if (entry.UnityObject is Material mat)
                    {
                        _state.Compare.LabelB.Value = "\u8D44\u4EA7\u6D4F\u89C8";
                        _state.Compare.MaterialB.Value = mat;
                        _state.CurrentViewMode.Value = ViewMode.Compare;
                    }
                });

                // ④ 进入批量替换 — 以此材质为候选
                menu.AddItem(new GUIContent("\u6279\u91CF\u66FF\u6362 \u2192 \u4EE5\u6B64\u6750\u8D28\u4E3A\u5019\u9009"), false, () =>
                {
                    _selectionController?.OnUserSelectAsset(entry);
                    _state.CurrentViewMode.Value = ViewMode.BatchReplace;
                });

                menu.AddSeparator("");
            }

            // ⑤ 复制资源路径
            menu.AddItem(new GUIContent("\u590D\u5236\u8D44\u6E90\u8DEF\u5F84"), false, () =>
            {
                if (!string.IsNullOrEmpty(entry.Path))
                {
                    GUIUtility.systemCopyBuffer = entry.Path;
                    _state.StatusMessage.Value = $"\u5DF2\u590D\u5236\uFF1A{entry.Path}";
                }
            });

            // ⑥ 在 Project 中定位
            menu.AddItem(new GUIContent("\u5728 Project \u4E2D\u5B9A\u4F4D"), false, () =>
            {
                if (entry.UnityObject != null)
                {
                    EditorGUIUtility.PingObject(entry.UnityObject);
                    Selection.activeObject = entry.UnityObject;
                }
            });

            menu.ShowAsContext();
        }
    }
}
