using UnityEngine.Rendering;

namespace HMI.Workspace.Editor.Services
{
    public sealed class PackageService : IPackageService
    {
        public string DetectPipeline()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline == null ? "Built-in" : pipeline.GetType().Name;
        }
    }
}
