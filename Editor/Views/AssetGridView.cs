using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Controllers.ViewInterfaces;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// CenterPanel 资产列表视图（implements IAssetGridView）。
    /// 每行显示：[类型标签] 资产名  路径简写。
    /// Controller 可通过 IAssetGridView 接口调用 ScrollToAsset / RefreshVisibleItems 等命令。
    /// </summary>
    public sealed class AssetGridView : IAssetGridView
    {
        private readonly VisualElement _root;
        private readonly ListView _listView;
        private List<AssetEntry> _items = new();
        private SelectionController _selectionController;

        public AssetGridView(VisualElement root)
        {
            _root = root;
            _listView = _root?.Q<ListView>("asset-grid");
        }

        public void Bind(WorkspaceState state, SelectionController selectionController)
        {
            _selectionController = selectionController;
            if (_listView == null) return;

            // 每行高度容纳 名称 + 路径 两行内容
            _listView.fixedItemHeight = 38;

            // makeItem: 构建每行的 DOM 结构
            _listView.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("asset-row");

                // 左侧：类型标签（MAT / TEX / PFB）
                var badge = new Label();
                badge.AddToClassList("asset-type-badge");
                row.Add(badge);

                // 右侧：名称 + 路径纵向排列
                var info = new VisualElement();
                info.AddToClassList("asset-row-info");

                var nameLabel = new Label();
                nameLabel.AddToClassList("asset-row-name");
                info.Add(nameLabel);

                var pathLabel = new Label();
                pathLabel.AddToClassList("asset-row-path");
                info.Add(pathLabel);

                row.Add(info);
                return row;
            };

            // bindItem: 填充每行数据
            _listView.bindItem = (element, index) =>
            {
                var entry = _items[index];
                var badge = element.Q<Label>(className: "asset-type-badge");
                var nameLabel = element.Q<Label>(className: "asset-row-name");
                var pathLabel = element.Q<Label>(className: "asset-row-path");

                badge.text = KindToTag(entry.Kind);
                // 根据类型设置标签背景色 class
                badge.RemoveFromClassList("badge-material");
                badge.RemoveFromClassList("badge-texture");
                badge.RemoveFromClassList("badge-prefab");
                badge.AddToClassList(KindToBadgeClass(entry.Kind));

                nameLabel.text = entry.DisplayName;
                pathLabel.text = ShortenPath(entry.Path);
            };

            _listView.selectionType = SelectionType.Single;
            _listView.onSelectionChange += items =>
            {
                foreach (var item in items)
                {
                    if (item is AssetEntry entry)
                        _selectionController.OnUserSelectAsset(entry);
                }
            };

            AssetEvents.FilteredChanged.Subscribe(OnFilteredChanged);
        }

        private void OnFilteredChanged(FilteredAssetsChangedEvent evt)
        {
            _items = new List<AssetEntry>(evt.Assets);
            if (_listView != null)
            {
                _listView.itemsSource = _items;
                _listView.Rebuild();
            }
        }

        // ── 辅助方法 ───────────────────────────────────────────────

        /// <summary>
        /// AssetKind → 3 字母标签（MAT / TEX / PFB / ???）
        /// </summary>
        private static string KindToTag(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "MAT",
                AssetKind.Texture  => "TEX",
                AssetKind.Prefab   => "PFB",
                AssetKind.Shader   => "SHD",
                AssetKind.Model    => "MDL",
                _                  => "???",
            };
        }

        /// <summary>
        /// AssetKind → 对应的 USS class 名，用于着色
        /// </summary>
        private static string KindToBadgeClass(AssetKind kind)
        {
            return kind switch
            {
                AssetKind.Material => "badge-material",
                AssetKind.Texture  => "badge-texture",
                AssetKind.Prefab   => "badge-prefab",
                _                  => "badge-material",
            };
        }

        /// <summary>
        /// 将完整路径缩短为 目录/文件名 形式
        /// "Assets/HMIWorkspaceTest/Materials/Red.mat" → "Materials/Red.mat"
        /// </summary>
        private static string ShortenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split('/');
            if (parts.Length <= 2) return path;
            return parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
        }

        // ── IAssetGridView 接口实现 ─────────────────────────────────

        /// <summary>
        /// 滚动到指定 GUID 的资产行
        /// </summary>
        public void ScrollToAsset(string guid)
        {
            if (_listView == null) return;
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Guid == guid)
                {
                    _listView.ScrollToItem(i);
                    break;
                }
            }
        }

        /// <summary>
        /// 触发 ListView 可见行重新绑定
        /// </summary>
        public void RefreshVisibleItems()
        {
            _listView?.RefreshItems();
        }

        /// <summary>
        /// 在指定屏幕位置弹出资产上下文菜单（Phase 2 实现）
        /// </summary>
        public void ShowContextMenu(AssetEntry entry, Vector2 screenPosition)
        {
            // Phase 2: GenericMenu 右键菜单
        }
    }
}
