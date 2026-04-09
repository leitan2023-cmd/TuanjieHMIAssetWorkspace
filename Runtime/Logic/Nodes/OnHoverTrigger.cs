using System.Collections.Generic;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 触发器：当鼠标悬停在目标对象上时触发。
    /// </summary>
    public class OnHoverTrigger : LogicNodeBase
    {
        public override NodeCategory Category => NodeCategory.Trigger;
        public override string DisplayName => "悬停触发";
        public override string Description => "当鼠标悬停在目标 GameObject 上时触发";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("进入", PortDirection.Output, PortKind.Flow),
            new PortDefinition("离开", PortDirection.Output, PortKind.Flow),
            new PortDefinition("悬停对象", PortDirection.Output, PortKind.Data, dataType: typeof(UnityEngine.GameObject))
        };

        public override string Execute(LogicExecutionContext context)
        {
            if (TargetObject != null)
                context.SetPortValue(Id, "悬停对象", TargetObject);
            return "进入";
        }
    }
}
