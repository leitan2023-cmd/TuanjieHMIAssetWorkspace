using System.Collections.Generic;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Services
{
    public sealed class AIService : IAIService
    {
        public string ExecuteCommand(string command, WorkspaceState state)
        {
            if (string.IsNullOrWhiteSpace(command))
                return "AI 指令为空。";

            var lower = command.Trim().ToLowerInvariant();
            var selected = state.SelectedAsset.Value;
            var selectedName = selected?.DisplayName ?? "当前选中项";

            if (lower.StartsWith("explain") || lower.Contains("解释"))
                return $"解释：{selectedName} 是一个 {selected?.Kind.ToString() ?? "工作区"} 类型资产。使用预览标签页查看元数据，处理材质时可使用“应用到场景”。";

            if (lower.StartsWith("find") || lower.Contains("查找"))
                return $"已收到查找请求：「{command}」。请使用搜索栏按名称、分类或标签缩小资产列表。";

            if (lower.StartsWith("suggest") || lower.Contains("推荐"))
                return $"建议：从 {selectedName} 开始，对比相似的材质或预制体，然后切换到对比视图查看替代方案。";

            if (lower.StartsWith("replace") || lower.Contains("替换"))
                return "替换流程尚未自动化，但工作区已准备好检查源资产并准备手动替换目标。";

            return $"AI 已收到：{command}。请尝试以 解释、查找、推荐 或 替换 开头的指令。";
        }

        public List<string> GetAutocomplete(string partial)
        {
            return new List<string> { "查找 ", "解释 ", "推荐 ", "替换 " };
        }
    }
}
