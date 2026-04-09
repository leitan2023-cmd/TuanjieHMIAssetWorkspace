using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 动作：设置目标对象的激活/隐藏状态。
    /// </summary>
    public class SetActiveAction : LogicNodeBase
    {
        public bool ActiveState = true;

        public override NodeCategory Category => NodeCategory.Action;
        public override string DisplayName => "显示/隐藏";
        public override string Description => "设置目标 GameObject 的显示或隐藏状态";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
            new PortDefinition("目标", PortDirection.Input, PortKind.Data, dataType: typeof(GameObject)),
            new PortDefinition("激活", PortDirection.Input, PortKind.Data, dataType: typeof(bool))
        };

        public override string Execute(LogicExecutionContext context)
        {
            var target = TargetObject;
            // 优先从数据端口获取目标
            var inputTarget = context.GetInputValue<GameObject>(Id, "目标");
            if (inputTarget != null) target = inputTarget;

            var active = context.GetInputValue(Id, "激活", ActiveState);

            if (target != null)
            {
                target.SetActive(active);
            }

            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { activeState = ActiveState });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            ActiveState = data.activeState;
        }

        [System.Serializable]
        private class SerData { public bool activeState = true; }
    }
}
