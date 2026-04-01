namespace HMI.Workspace.Editor.Controllers.ViewInterfaces
{
    /// <summary>
    /// BottomBar 视图接口（Architecture Addendum Amendment 5）。
    /// Controller 通过此接口向 BottomBar 发送 UI 命令。
    /// </summary>
    public interface IBottomBarView
    {
        /// <summary>
        /// 显示一条带自动消失的临时消息
        /// </summary>
        void ShowTransientMessage(string message, float durationSeconds);
    }
}
