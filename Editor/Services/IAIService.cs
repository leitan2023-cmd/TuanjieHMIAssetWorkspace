using System.Collections.Generic;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public interface IAIService
    {
        string ExecuteCommand(string command, WorkspaceState state);
        List<string> GetAutocomplete(string partial);
    }
}
