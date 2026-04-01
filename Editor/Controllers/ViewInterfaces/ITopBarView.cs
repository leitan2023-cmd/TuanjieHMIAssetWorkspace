using System.Collections.Generic;

namespace HMI.Workspace.Editor.Controllers.ViewInterfaces
{
    /// <summary>
    /// TopBar 视图接口（Architecture Addendum Amendment 5）。
    /// Controller 通过此接口向 TopBar 发送 UI 命令，
    /// 而不直接依赖 VisualElement 或具体 View 类。
    /// </summary>
    public interface ITopBarView
    {
        /// <summary>
        /// 显示命令自动补全下拉列表
        /// </summary>
        void ShowAutocompleteDropdown(List<string> suggestions);

        /// <summary>
        /// 隐藏命令自动补全下拉列表
        /// </summary>
        void HideAutocompleteDropdown();

        /// <summary>
        /// 设置命令栏文本（用于 AI "fill command" 功能）
        /// </summary>
        void SetCommandText(string text);
    }
}
