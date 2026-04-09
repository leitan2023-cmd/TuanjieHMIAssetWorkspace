using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HMI.Workspace.Editor.Core
{
    public sealed class Observable<T>
    {
        private T _value;
        public event Action<T, T> Changed;

        public Observable() : this(default) { }

        public Observable(T initialValue)
        {
            _value = initialValue;
        }

        /// <summary>调试标签，用于 PerfTrace 计数（可选设置）。</summary>
        public string DebugLabel { get; set; }

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                var oldValue = _value;
                _value = value;
                if (DebugLabel != null) PerfTrace.Count($"Observable.Changed:{DebugLabel}");
                Changed?.Invoke(oldValue, _value);
            }
        }

        public void BindToLabel(Label label, Func<T, string> formatter = null)
        {
            formatter ??= v => v?.ToString() ?? string.Empty;
            label.text = formatter(Value);
            Changed += (_, newValue) => label.text = formatter(newValue);
        }
    }
}
