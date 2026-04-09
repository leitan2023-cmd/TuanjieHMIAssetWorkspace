using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 调试用节点：输出日志到 Console。
    /// 方便用户调试逻辑图的执行流。
    /// </summary>
    public class DebugLogAction : LogicNodeBase
    {
        public string Message = "Logic 调试信息";

        public override NodeCategory Category => NodeCategory.Data;
        public override string DisplayName => "调试日志";
        public override string Description => "在 Console 中输出调试信息";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
        };

        public override string Execute(LogicExecutionContext context)
        {
            var target = TargetObject != null ? TargetObject.name : "(无目标)";
            Debug.Log($"[HMI Logic] {Message} | 目标: {target}");
            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { message = Message });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            Message = data.message;
        }

        [System.Serializable]
        private class SerData { public string message = "Logic 调试信息"; }
    }
}
