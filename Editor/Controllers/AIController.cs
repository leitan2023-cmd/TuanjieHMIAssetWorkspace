using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Editor.Services;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class AIController : IController
    {
        private readonly IAIService _aiService;
        private readonly WorkspaceState _state;

        public AIController(IAIService aiService, WorkspaceState state)
        {
            _aiService = aiService;
            _state = state;
        }

        public void Initialize() { }

        public void ExecuteCommand(string command)
        {
            var result = _aiService.ExecuteCommand(command, _state);
            AIEvents.CommandResult.Publish(new CommandResultReadyEvent(result));
            _state.StatusMessage.Value = result;
        }

        public void Dispose() { }
    }
}
