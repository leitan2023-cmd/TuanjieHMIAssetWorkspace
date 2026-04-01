using System.Collections.Generic;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public sealed class AIService : IAIService
    {
        public string ExecuteCommand(string command, WorkspaceState state)
        {
            return $"Command received: {command}";
        }

        public List<string> GetAutocomplete(string partial)
        {
            return new List<string> { "find ", "explain ", "suggest ", "replace " };
        }
    }
}
