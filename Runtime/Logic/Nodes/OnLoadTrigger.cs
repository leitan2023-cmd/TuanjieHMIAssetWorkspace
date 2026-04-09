using System.Collections.Generic;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 触发器：场景加载完成时自动触发。
    /// </summary>
    public class OnLoadTrigger : LogicNodeBase
    {
        public override NodeCategory Category => NodeCategory.Trigger;
        public override string DisplayName => "场景加载";
        public override string Description => "场景加载完成时自动触发后续动作";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("触发", PortDirection.Output, PortKind.Flow)
        };

        public override string Execute(LogicExecutionContext context)
        {
            return "触发";
        }
    }
}
