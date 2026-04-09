namespace HMI.Workspace.Editor.Data
{
    public enum ViewMode { Home, Grid, Scene, Compare, VehicleSetup, BatchReplace, SceneBuilder, LogicFlow }
    public enum OperatingMode { Full, Preview, Limited }
    public enum AssetKind { Unknown, Material, Model, Prefab, Texture, Shader, Scene, Fx }

    // ── ViewMode 显示名称（集中定义，消除各类中的重复 ModeToLabel） ──
    public static class ViewModeExtensions
    {
        /// <summary>获取 ViewMode 的中文显示标签。</summary>
        public static string ToLabel(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.Home         => "\u9996\u9875",
                ViewMode.Grid         => "\u8D44\u4EA7\u6D4F\u89C8",
                ViewMode.Scene        => "\u573A\u666F\u9884\u89C8",
                ViewMode.Compare      => "\u5BF9\u6BD4",
                ViewMode.VehicleSetup => "\u8F66\u8F86\u914D\u7F6E",
                ViewMode.BatchReplace => "\u6279\u91CF\u66FF\u6362",
                ViewMode.SceneBuilder => "\u573A\u666F\u642D\u5EFA",
                ViewMode.LogicFlow   => "\u903B\u8F91\u7F16\u8F91",
                _                     => mode.ToString(),
            };
        }
    }
}
