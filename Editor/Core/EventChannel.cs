using System;

namespace HMI.Workspace.Editor.Core
{
    public sealed class EventChannel<T>
    {
        private Action<T> _handlers;

        /// <summary>调试标签，用于 PerfTrace 计数（可选设置）。</summary>
        public string DebugLabel { get; set; }

        public void Subscribe(Action<T> handler) => _handlers += handler;
        public void Unsubscribe(Action<T> handler) => _handlers -= handler;
        public void Publish(T evt)
        {
            if (DebugLabel != null) PerfTrace.Count($"EventChannel.Publish:{DebugLabel}");
            _handlers?.Invoke(evt);
        }
        public void Clear() => _handlers = null;
    }
}
