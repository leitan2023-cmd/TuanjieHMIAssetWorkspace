using UnityEditor.SceneManagement;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Controllers
{
    public sealed class SceneController : IController
    {
        private readonly WorkspaceState _state;

        public SceneController(WorkspaceState state)
        {
            _state = state;
        }

        public void Initialize()
        {
            UpdateSceneInfo();
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene next)
        {
            UpdateSceneInfo();
        }

        private void UpdateSceneInfo()
        {
            var scene = EditorSceneManager.GetActiveScene();
            _state.ActiveScene.Value = new SceneInfo(scene.name, scene.rootCount, scene.isDirty);
        }

        public void Dispose()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
        }
    }
}
