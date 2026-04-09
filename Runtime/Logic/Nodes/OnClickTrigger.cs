using System.Collections.Generic;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 触发器：当目标对象被点击时触发执行流。
    /// 在 Editor 演示模式下由 LogicExecutor 手动调用。
    /// </summary>
    public class OnClickTrigger : LogicNodeBase
    {
        public override NodeCategory Category => NodeCategory.Trigger;
        public override string DisplayName => "点击触发";
        public override string Description => "当目标 GameObject 被点击时触发后续动作";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("触发", PortDirection.Output, PortKind.Flow),
            new PortDefinition("点击对象", PortDirection.Output, PortKind.Data, dataType: typeof(UnityEngine.GameObject))
        };

        public override string Execute(LogicExecutionContext context)
        {
            // Trigger 节点：将自身的 TargetObject 写入数据端口
            if (TargetObject != null)
                context.SetPortValue(Id, "点击对象", TargetObject);
            return "触发";
        }
    }
}
