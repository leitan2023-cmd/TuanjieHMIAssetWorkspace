using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    public sealed class PreviewCache
    {
        private readonly int _maxEntries;
        private readonly Dictionary<string, Texture2D> _entries = new();
        private readonly LinkedList<string> _lru = new();

        public PreviewCache(int maxEntries = 200)
        {
            _maxEntries = maxEntries;
        }

        public bool TryGet(string guid, out Texture2D texture)
        {
            if (_entries.TryGetValue(guid, out texture))
            {
                _lru.Remove(guid);
                _lru.AddFirst(guid);
                return true;
            }
            return false;
        }

        public void Set(string guid, Texture2D texture)
        {
            if (string.IsNullOrEmpty(guid) || texture == null) return;
            if (_entries.ContainsKey(guid))
            {
                _entries[guid] = texture;
                _lru.Remove(guid);
                _lru.AddFirst(guid);
                return;
            }
            _entries.Add(guid, texture);
            _lru.AddFirst(guid);
            while (_entries.Count > _maxEntries && _lru.Last != null)
            {
                var tail = _lru.Last.Value;
                _lru.RemoveLast();
                _entries.Remove(tail);
            }
        }

        public void Clear()
        {
            _entries.Clear();
            _lru.Clear();
        }
    }
}
