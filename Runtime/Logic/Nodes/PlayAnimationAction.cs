using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 动作：播放目标对象上的动画。
    /// </summary>
    public class PlayAnimationAction : LogicNodeBase
    {
        public string ClipName = "";
        public bool Loop;

        public override NodeCategory Category => NodeCategory.Action;
        public override string DisplayName => "播放动画";
        public override string Description => "播放目标对象上的 Animation 或 Animator 动画";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
            new PortDefinition("目标", PortDirection.Input, PortKind.Data, dataType: typeof(GameObject)),
        };

        public override string Execute(LogicExecutionContext context)
        {
            var target = TargetObject;
            var inputTarget = context.GetInputValue<GameObject>(Id, "目标");
            if (inputTarget != null) target = inputTarget;

            if (target != null)
            {
                // 优先尝试 Animator
                var animator = target.GetComponent<Animator>();
                if (animator != null && !string.IsNullOrEmpty(ClipName))
                {
                    animator.Play(ClipName);
                    return "完成";
                }

                // 回退到 Legacy Animation
                var animation = target.GetComponent<Animation>();
                if (animation != null)
                {
                    if (!string.IsNullOrEmpty(ClipName))
                        animation.Play(ClipName);
                    else
                        animation.Play();
                }
            }

            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { clipName = ClipName, loop = Loop });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            ClipName = data.clipName;
            Loop = data.loop;
        }

        [System.Serializable]
        private class SerData { public string clipName; public bool loop; }
    }
}
