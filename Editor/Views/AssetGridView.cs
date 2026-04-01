using System.Collections.Generic;
using UnityEngine.UIElements;
using HMI.Workspace.Editor.Controllers;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    public sealed class AssetGridView
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
            if (_listView != null)
            {
                // makeItem: 创建列表项的占位 Label 元素
                _listView.makeItem = () =>
                {
                    var label = new Label();
                    label.AddToClassList("asset-card");
                    return label;
                };
                _listView.bindItem = (element, index) => ((Label)element).text = _items[index].DisplayName;
                _listView.selectionType = SelectionType.Single;
                _listView.onSelectionChange += items =>
                {
                    foreach (var item in items)
                    {
                        if (item is AssetEntry entry) _selectionController.OnUserSelectAsset(entry);
                    }
                };
            }

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
    }
}
