using UnityEngine.UIElements;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Views
{
    public sealed class ScenePreviewView
    {
        private readonly VisualElement _root;
        private Label _titleLabel;
        private Label _descriptionLabel;

        public ScenePreviewView(VisualElement root) => _root = root;

        public void Bind(WorkspaceState state)
        {
            if (_root == null) return;

            _root.Clear();

            var card = new VisualElement();
            card.AddToClassList("placeholder-card");

            var content = new VisualElement();
            content.AddToClassList("placeholder-content");

            _titleLabel = new Label("场景预览");
            _titleLabel.AddToClassList("placeholder-title");

            _descriptionLabel = new Label("切换到场景模式，在独立预览舞台中检查选中的资产。");
            _descriptionLabel.AddToClassList("placeholder-text");

            content.Add(_titleLabel);
            content.Add(_descriptionLabel);
            content.Add(CreateMetricRow());
            card.Add(content);
            _root.Add(card);

            state.SelectedAsset.Changed += (_, asset) =>
            {
                _titleLabel.text = asset != null ? $"场景预览：{asset.DisplayName}" : "场景预览";
                _descriptionLabel.text = asset != null
                    ? $"下一版本将在此处渲染 {asset.Kind} 类型资产的完整交互式舞台预览。"
                    : "选择一个资产以准备独立场景舞台预览。";
            };
        }

        private static VisualElement CreateMetricRow()
        {
            var row = new VisualElement();
            row.AddToClassList("placeholder-metric-row");
            row.Add(CreateMetric("相机", "透视 / FOV 60"));
            row.Add(CreateMetric("交互", "环绕 + 缩放"));
            row.Add(CreateMetric("舞台", "待定"));
            return row;
        }

        private static VisualElement CreateMetric(string key, string value)
        {
            var metric = new VisualElement();
            metric.AddToClassList("placeholder-metric");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("placeholder-metric-key");
            var valueLabel = new Label(value);
            valueLabel.AddToClassList("placeholder-metric-value");

            metric.Add(keyLabel);
            metric.Add(valueLabel);
            return metric;
        }
    }
}
