using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 流程控制：延迟指定秒数后继续执行。
    /// 注意：当前版本在 Editor 模式下为同步执行（不真正等待）。
    /// 后续可改为协程实现真实延迟。
    /// </summary>
    public class DelayAction : LogicNodeBase
    {
        public float DelaySeconds = 1f;

        public override NodeCategory Category => NodeCategory.Flow;
        public override string DisplayName => "延迟";
        public override string Description => "等待指定秒数后继续执行";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
        };

        public override string Execute(LogicExecutionContext context)
        {
            // TODO: Editor 模式下暂时不做真实延迟，直接通过
            // Runtime 模式可用协程实现
            Debug.Log($"[Delay] 延迟 {DelaySeconds} 秒（Editor 模式下跳过）");
            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { delaySeconds = DelaySeconds });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            DelaySeconds = data.delaySeconds;
        }

        [System.Serializable]
        private class SerData { public float delaySeconds = 1f; }
    }
}
