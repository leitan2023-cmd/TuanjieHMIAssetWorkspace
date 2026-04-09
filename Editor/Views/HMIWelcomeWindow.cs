using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HMI.Workspace.Editor.Views
{
    /// <summary>
    /// HMI Asset Studio 欢迎页 — 独立 EditorWindow，一屏展示。
    /// 打开 package 后首先显示此页面，点击"打开工作区"进入 HMIWorkspaceWindow。
    /// </summary>
    public sealed class HMIWelcomeWindow : EditorWindow
    {
        private const string ImgRoot = "Packages/com.hmi.workspace/Editor/Images/";

        [MenuItem("Window/HMI Asset Studio")]
        public static void Open()
        {
            var window = GetWindow<HMIWelcomeWindow>();
            window.titleContent = new GUIContent("HMI Asset Studio");
            window.minSize = new Vector2(900, 560);
        }

        private void CreateGUI()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.hmi.workspace/Editor/USS/HMIWelcome.uss");
            if (style != null)
                rootVisualElement.styleSheets.Add(style);

            rootVisualElement.AddToClassList("welcome-root");

            // ── 背景装饰 ──
            var glowTop = new VisualElement();
            glowTop.AddToClassList("welcome-glow-top");
            rootVisualElement.Add(glowTop);

            // ── 主容器 ──
            var container = new VisualElement();
            container.AddToClassList("welcome-container");

            // ── Hero 区域 ──
            BuildHero(container);

            // ── 能力卡片 ──
            BuildCards(container);

            // ── Footer ──
            BuildFooter(container);

            rootVisualElement.Add(container);
        }

        // ═══════════════════════════════════════════════════════════
        // Hero
        // ═══════════════════════════════════════════════════════════

        private void BuildHero(VisualElement parent)
        {
            var hero = new VisualElement();
            hero.AddToClassList("welcome-hero");
            SetBgImage(hero, "welcome-hero.png");

            // 文字区（垂直居中）
            var textArea = new VisualElement();
            textArea.AddToClassList("welcome-hero-text");

            var tagRow = new VisualElement();
            tagRow.AddToClassList("welcome-tag-row");
            var tagLine = new VisualElement();
            tagLine.AddToClassList("welcome-tag-line");
            tagRow.Add(tagLine);
            var tagLabel = new Label("HMI STUDIO SUITE");
            tagLabel.AddToClassList("welcome-tag-label");
            tagRow.Add(tagLabel);
            textArea.Add(tagRow);

            var title = new Label("HMI Asset Studio");
            title.AddToClassList("welcome-title");
            textArea.Add(title);

            var subtitle = new Label("统一组织 HMI 资产、场景能力与使用路径");
            subtitle.AddToClassList("welcome-subtitle");
            textArea.Add(subtitle);

            var desc = new Label("聚合材质、模型、特效与场景模板等核心能力，帮助团队快速理解当前内容构成与使用方式。");
            desc.AddToClassList("welcome-desc");
            textArea.Add(desc);

            hero.Add(textArea);

            // 按钮行（贴底）
            var btnRow = new VisualElement();
            btnRow.AddToClassList("welcome-btn-group");
            var primaryBtn = new Button(() =>
            {
                HMIWorkspaceWindow.Open();
                Close();
            }) { text = "打开工作区" };
            primaryBtn.AddToClassList("welcome-btn-primary");
            btnRow.Add(primaryBtn);

            var secondBtn = new Button(() => OpenDocs()) { text = "阅读文档" };
            secondBtn.AddToClassList("welcome-btn-secondary");
            btnRow.Add(secondBtn);
            hero.Add(btnRow);

            parent.Add(hero);
        }

        // ═══════════════════════════════════════════════════════════
        // 能力卡片
        // ═══════════════════════════════════════════════════════════

        private void BuildCards(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("welcome-card-section");

            // 分割标题
            var headerRow = new VisualElement();
            headerRow.AddToClassList("welcome-section-header");
            var headerLabel = new Label("能 力 概 览");
            headerLabel.AddToClassList("welcome-section-title");
            headerRow.Add(headerLabel);
            section.Add(headerRow);

            var grid = new VisualElement();
            grid.AddToClassList("welcome-card-grid");

            // 卡片 1：平台定位
            grid.Add(CreateCard("blue", "◆", "平台定位",
                "承接 HMI 资产与场景能力的统一展示和组织，帮助团队快速理解 package 的能力边界与内容构成。",
                "查看定位", "welcome-card-position.png"));

            // 卡片 2：能力范围
            grid.Add(CreateCard("purple", "▦", "能力范围",
                "材质浏览 · 模型概览 · 特效展示 · 场景模板预览",
                "查看内容", "welcome-card-scope.png"));

            // 卡片 3：使用路径
            grid.Add(CreateCard("green", "☰", "使用路径",
                "了解内容结构 → 进入功能区域预览 → 基于示例与文档开始接入",
                "开始使用", "welcome-card-path.png"));

            section.Add(grid);
            parent.Add(section);
        }

        // ═══════════════════════════════════════════════════════════
        // Footer
        // ═══════════════════════════════════════════════════════════

        private static void BuildFooter(VisualElement parent)
        {
            var footer = new VisualElement();
            footer.AddToClassList("welcome-footer");

            var left = new VisualElement();
            left.AddToClassList("welcome-footer-links");
            left.Add(FooterLink("文档中心"));
            left.Add(FooterLink("示例工程"));
            left.Add(FooterLink("更新记录"));
            left.Add(FooterLink("技术支持"));
            footer.Add(left);

            var right = new Label("HMI Asset Studio · Unity China · © 2026");
            right.AddToClassList("welcome-footer-copy");
            footer.Add(right);

            parent.Add(footer);
        }

        // ═══════════════════════════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════════════════════════

        private VisualElement CreateCard(string color, string icon,
            string title, string descText, string actionText, string imgFile)
        {
            var card = new VisualElement();
            card.AddToClassList("welcome-card");
            card.AddToClassList($"welcome-card--{color}");

            // 卡片图片
            var imgArea = new VisualElement();
            imgArea.AddToClassList("welcome-card-img");
            SetBgImage(imgArea, imgFile);
            card.Add(imgArea);

            // 图标 + 标题行
            var headRow = new VisualElement();
            headRow.AddToClassList("welcome-card-head");
            var iconBox = new VisualElement();
            iconBox.AddToClassList("welcome-card-icon-box");
            iconBox.AddToClassList($"welcome-card-icon-box--{color}");
            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("welcome-card-icon");
            iconBox.Add(iconLabel);
            headRow.Add(iconBox);
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("welcome-card-title");
            headRow.Add(titleLabel);
            card.Add(headRow);

            // 描述
            var desc = new Label(descText);
            desc.AddToClassList("welcome-card-desc");
            card.Add(desc);

            // 底部操作
            var actionRow = new VisualElement();
            actionRow.AddToClassList("welcome-card-action");
            var actionLabel = new Label(actionText);
            actionLabel.AddToClassList("welcome-card-action-text");
            actionLabel.AddToClassList($"welcome-card-action--{color}");
            actionRow.Add(actionLabel);
            var arrow = new Label("→");
            arrow.AddToClassList("welcome-card-action-arrow");
            arrow.AddToClassList($"welcome-card-action--{color}");
            actionRow.Add(arrow);
            card.Add(actionRow);

            return card;
        }

        private void SetBgImage(VisualElement el, string fileName)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ImgRoot + fileName);
            if (tex != null)
                el.style.backgroundImage = new StyleBackground(tex);
        }

        private static Label FooterLink(string text)
        {
            var link = new Label(text);
            link.AddToClassList("welcome-footer-link");
            return link;
        }

        private static void OpenDocs()
        {
            var guidePath = "Packages/com.hmi.workspace/Docs/HMI-Asset-Studio-Guide.md";
            if (System.IO.File.Exists(guidePath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(guidePath);
                if (asset != null)
                    AssetDatabase.OpenAsset(asset);
                else
                    EditorUtility.RevealInFinder(guidePath);
            }
            else
            {
                var docsDir = "Packages/com.hmi.workspace/Docs";
                if (System.IO.Directory.Exists(docsDir))
                    EditorUtility.RevealInFinder(docsDir);
            }
        }
    }
}
