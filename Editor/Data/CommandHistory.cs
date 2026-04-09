using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    /// <summary>
    /// 记录已执行操作的历史。
    /// BottomBarView 消费此数据显示控制台日志。
    ///
    /// 持久化策略：使用 SessionState（编辑器会话级别），
    /// 窗口关闭/重新打开后仍保留，但 Unity 重启后清空。
    /// </summary>
    public sealed class CommandHistory
    {
        private const int MaxEntries = 100;
        private const int MaxSessionChars = 16 * 1024; // 16KB SessionState 字符上限
        private const int MaxMessageLength = 200;      // 单条消息最大字符数
        private const string SessionKey = "HMIWorkspace_CommandHistory";
        private readonly List<CommandRecord> _records = new();

        public IReadOnlyList<CommandRecord> Records => _records;

        public CommandHistory()
        {
            RestoreFromSession();
        }

        public void Add(string actionName, string message, bool success)
        {
            // 截断过长消息，防止单条记录膨胀
            if (message != null && message.Length > MaxMessageLength)
                message = message.Substring(0, MaxMessageLength) + "…";

            _records.Add(new CommandRecord(actionName, message, success));
            if (_records.Count > MaxEntries)
                _records.RemoveAt(0);
            SaveToSession();
        }

        public void Clear()
        {
            _records.Clear();
            SessionState.EraseString(SessionKey);
        }

        // ══════════════════════════════════════════════════════════════
        // SessionState 轻量持久化
        // ══════════════════════════════════════════════════════════════

        private void SaveToSession()
        {
            try
            {
                // 只保留最近 50 条到 SessionState，避免过大
                int start = Mathf.Max(0, _records.Count - 50);
                var lines = new List<string>();
                for (int i = start; i < _records.Count; i++)
                {
                    var r = _records[i];
                    // 格式：success|actionName|message（用 | 分隔，message 中的 | 会被转义）
                    var escapedMsg = r.Message.Replace("|", "\\|");
                    lines.Add($"{(r.Success ? "1" : "0")}|{r.ActionName}|{escapedMsg}");
                }
                // 字符总长度保护：从尾部保留，丢弃最旧记录直到不超限
                var joined = string.Join("\n", lines);
                while (joined.Length > MaxSessionChars && lines.Count > 1)
                {
                    lines.RemoveAt(0);
                    joined = string.Join("\n", lines);
                }
                SessionState.SetString(SessionKey, joined);
            }
            catch (Exception)
            {
                // SessionState 写入失败不影响正常运行
            }
        }

        private void RestoreFromSession()
        {
            try
            {
                var data = SessionState.GetString(SessionKey, "");
                if (string.IsNullOrEmpty(data)) return;

                var lines = data.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    // 拆分：最多 3 段
                    var firstPipe = line.IndexOf('|');
                    if (firstPipe < 0) continue;
                    var secondPipe = line.IndexOf('|', firstPipe + 1);
                    if (secondPipe < 0) continue;

                    var successStr = line.Substring(0, firstPipe);
                    var actionName = line.Substring(firstPipe + 1, secondPipe - firstPipe - 1);
                    var message = line.Substring(secondPipe + 1).Replace("\\|", "|");
                    bool success = successStr == "1";

                    _records.Add(new CommandRecord(actionName, message, success));
                }

                // 确保不超限
                while (_records.Count > MaxEntries)
                    _records.RemoveAt(0);
            }
            catch (Exception)
            {
                // 恢复失败时忽略，从空列表开始
            }
        }
    }

    public readonly struct CommandRecord
    {
        public CommandRecord(string actionName, string message, bool success)
        {
            ActionName = actionName;
            Message = message;
            Success = success;
        }

        public string ActionName { get; }
        public string Message { get; }
        public bool Success { get; }
    }
}
