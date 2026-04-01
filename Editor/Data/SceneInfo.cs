namespace HMI.Workspace.Editor.Data
{
    public readonly struct SceneInfo
    {
        public SceneInfo(string name, int rootCount, bool isDirty)
        {
            Name = name;
            RootCount = rootCount;
            IsDirty = isDirty;
        }

        public string Name { get; }
        public int RootCount { get; }
        public bool IsDirty { get; }

        public override string ToString() => Name;
    }
}
