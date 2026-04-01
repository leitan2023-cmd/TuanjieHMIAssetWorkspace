using System;

namespace HMI.Workspace.Editor.Core
{
    public sealed class EventChannel<T>
    {
        private Action<T> _handlers;

        public void Subscribe(Action<T> handler) => _handlers += handler;
        public void Unsubscribe(Action<T> handler) => _handlers -= handler;
        public void Publish(T evt) => _handlers?.Invoke(evt);
        public void Clear() => _handlers = null;
    }
}
