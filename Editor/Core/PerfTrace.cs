using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HMI.Workspace.Editor.Core
{
    /// <summary>
    /// 轻量级性能打点工具。
    /// 在 Editor 中输出 Console 日志，不影响运行时。
    ///
    /// 使用方式：
    ///   using (PerfTrace.Begin("ScanAllRoots")) { ... }
    ///   // 或
    ///   var t = PerfTrace.Begin("RebuildCards");
    ///   ...
    ///   t.Dispose();
    ///
    /// 汇总：
    ///   PerfTrace.DumpReport();   // 输出前 20 慢操作
    ///   PerfTrace.Reset();        // 清空
    /// </summary>
    public static class PerfTrace
    {
        private static readonly Dictionary<string, List<double>> _records = new();
        private static readonly Dictionary<string, int> _counters = new();
        private static bool _enabled = true;

        /// <summary>全局开关（默认开启，可在运行时关闭以消除打点开销）。</summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// 开始一次计时。返回 IDisposable，using 块结束时自动记录耗时。
        /// </summary>
        public static Scope Begin(string label)
        {
            return new Scope(label, _enabled);
        }

        /// <summary>
        /// 仅计数，不计时。用于统计事件触发次数。
        /// </summary>
        public static void Count(string label)
        {
            if (!_enabled) return;
            lock (_counters)
            {
                _counters.TryGetValue(label, out var c);
                _counters[label] = c + 1;
            }
        }

        /// <summary>重置所有打点数据。</summary>
        public static void Reset()
        {
            lock (_records) _records.Clear();
            lock (_counters) _counters.Clear();
        }

        /// <summary>
        /// 输出到 Console：前 N 慢操作（按峰值排序）+ 事件计数。
        /// </summary>
        public static void DumpReport(int topN = 20)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════ [HMI PerfTrace Report] ═══════════");

            // ── 耗时排行 ──
            List<(string label, int count, double avg, double max, double total)> stats;
            lock (_records)
            {
                stats = _records
                    .Select(kv =>
                    {
                        var list = kv.Value;
                        double total = 0, max = 0;
                        foreach (var v in list) { total += v; if (v > max) max = v; }
                        double avg = list.Count > 0 ? total / list.Count : 0;
                        return (kv.Key, list.Count, avg, max, total);
                    })
                    .OrderByDescending(x => x.max)
                    .Take(topN)
                    .ToList();
            }

            if (stats.Count > 0)
            {
                sb.AppendLine($"  Top {Math.Min(topN, stats.Count)} slowest (by peak ms):");
                sb.AppendLine($"  {"#",-3} {"Operation",-48} {"Count",6} {"Avg ms",8} {"Peak ms",8} {"Total ms",9}");
                for (int i = 0; i < stats.Count; i++)
                {
                    var s = stats[i];
                    sb.AppendLine($"  {i + 1,-3} {s.label,-48} {s.count,6} {s.avg,8:F2} {s.max,8:F2} {s.total,9:F1}");
                }
            }
            else
            {
                sb.AppendLine("  (no timing data)");
            }

            // ── 事件计数 ──
            List<(string label, int count)> counts;
            lock (_counters)
            {
                counts = _counters
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList();
            }

            if (counts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Event fire counts:");
                foreach (var (label, count) in counts)
                    sb.AppendLine($"    {label,-52} {count,6}x");
            }

            sb.AppendLine("═══════════════════════════════════════════════");
            Debug.Log(sb.ToString());
        }

        internal static void Record(string label, double ms)
        {
            lock (_records)
            {
                if (!_records.TryGetValue(label, out var list))
                {
                    list = new List<double>(64);
                    _records[label] = list;
                }
                // 只保留最近 200 条，防止内存膨胀
                if (list.Count >= 200) list.RemoveAt(0);
                list.Add(ms);
            }
        }

        /// <summary>using 块作用域计时器。</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly string _label;
            private readonly long _startTicks;
            private readonly bool _enabled;

            internal Scope(string label, bool enabled)
            {
                _label = label;
                _enabled = enabled;
                _startTicks = enabled ? Stopwatch.GetTimestamp() : 0;
            }

            public void Dispose()
            {
                if (!_enabled) return;
                long elapsed = Stopwatch.GetTimestamp() - _startTicks;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                Record(_label, ms);

                // 超过 16ms 的操作立即警告（一帧 60fps）
                if (ms > 16.0)
                    Debug.LogWarning($"[PerfTrace] SLOW: {_label} = {ms:F1}ms");
            }
        }
    }
}
