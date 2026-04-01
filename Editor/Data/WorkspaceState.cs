using UnityEngine;
using HMI.Workspace.Editor.Core;

namespace HMI.Workspace.Editor.Data
{
    public sealed class WorkspaceState
    {
        public Observable<AssetEntry> SelectedAsset { get; } = new();
        public Observable<Object> UnitySelection { get; } = new();
        public Observable<ViewMode> CurrentViewMode { get; } = new(ViewMode.Grid);
        public Observable<ActiveTool> CurrentTool { get; } = new(ActiveTool.None);
        public Observable<SceneInfo> ActiveScene { get; } = new();
        public Observable<string> PipelineName { get; } = new("Unknown");
        public Observable<OperatingMode> Mode { get; } = new(OperatingMode.Preview);
        public Observable<string> StatusMessage { get; } = new("Ready");
    }
}
