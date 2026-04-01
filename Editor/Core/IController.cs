using System;

namespace HMI.Workspace.Editor.Core
{
    public interface IController : IDisposable
    {
        void Initialize();
    }
}
