using System.Collections.Generic;
using HMI.Workspace.Editor.Core;

namespace HMI.Workspace.Editor.Data
{
    /// <summary>
    /// 场景模板描述。
    /// </summary>
    public sealed class SceneTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string[] Features { get; set; }

        // ── 产品级扩展 ──
        /// <summary>面向用户的场景用途说明</summary>
        public string UsageHint { get; set; }
        /// <summary>推荐的默认灯光预设 Id</summary>
        public string DefaultLighting { get; set; }
        /// <summary>推荐的默认相机预设 Id</summary>
        public string DefaultCamera { get; set; }
        /// <summary>推荐的默认天气 Id</summary>
        public string DefaultWeather { get; set; }
        /// <summary>推荐的默认地面 Id</summary>
        public string DefaultFloor { get; set; }
        /// <summary>推荐的默认天空 Id</summary>
        public string DefaultSky { get; set; }
        /// <summary>环境描述词</summary>
        public string EnvironmentLabel { get; set; }
        /// <summary>图标字符</summary>
        public string Icon { get; set; }
    }

    public sealed class LightingPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public sealed class CameraPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float FOV { get; set; }
        public string Mode { get; set; }
    }

    public sealed class EnvironmentOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
    }

    public sealed class SceneBuilderState
    {
        public Observable<List<SceneTemplate>> Templates { get; } = new(new List<SceneTemplate>());
        public Observable<SceneTemplate> SelectedTemplate { get; } = new();
        public Observable<string> LightingPresetId { get; } = new("studio");
        public Observable<string> CameraPresetId { get; } = new("orbit-60");
        public Observable<string> WeatherId { get; } = new("sunny");
        public Observable<string> FloorId { get; } = new("dark");
        public Observable<string> SkyId { get; } = new("gradient");
        public Observable<string> GenerateStatus { get; } = new("");
        public Observable<bool> IsGenerating { get; } = new(false);
    }
}
