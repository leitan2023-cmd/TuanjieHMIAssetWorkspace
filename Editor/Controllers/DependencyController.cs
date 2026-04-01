using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class DependencyController : IController
    {
        private readonly IPackageService _packageService;
        private readonly WorkspaceState _state;

        public DependencyController(IPackageService packageService, WorkspaceState state)
        {
            _packageService = packageService;
            _state = state;
        }

        public void Initialize()
        {
            _state.PipelineName.Value = _packageService.DetectPipeline();
            _state.Mode.Value = OperatingMode.Preview;
        }

        public void Dispose() { }
    }
}
