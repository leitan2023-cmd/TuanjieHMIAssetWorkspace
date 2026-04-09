using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 动作：将摄像机聚焦到目标对象。
    /// HMI 典型场景：点击零件 → 相机飞到该零件视角。
    /// </summary>
    public class FocusCameraAction : LogicNodeBase
    {
        public float Distance = 3f;
        public float Height = 1f;
        public float Duration = 0.5f;

        public override NodeCategory Category => NodeCategory.Action;
        public override string DisplayName => "聚焦相机";
        public override string Description => "将主相机移动到目标对象的最佳观察位置";

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
                var cam = Camera.main;
                if (cam != null)
                {
                    // 计算目标包围盒中心
                    var bounds = new Bounds(target.transform.position, Vector3.zero);
                    foreach (var r in target.GetComponentsInChildren<Renderer>())
                        bounds.Encapsulate(r.bounds);

                    var center = bounds.center;
                    var size = bounds.size.magnitude;
                    var actualDistance = Mathf.Max(Distance, size * 1.5f);

                    // 从当前相机方向计算新位置
                    var direction = (cam.transform.position - center).normalized;
                    if (direction.sqrMagnitude < 0.01f) direction = Vector3.back;

                    var newPos = center + direction * actualDistance + Vector3.up * Height;

                    // 直接设置（Editor 演示模式下不需要动画过渡）
                    cam.transform.position = newPos;
                    cam.transform.LookAt(center);
                }
            }

            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { distance = Distance, height = Height, duration = Duration });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            Distance = data.distance;
            Height = data.height;
            Duration = data.duration;
        }

        [System.Serializable]
        private class SerData { public float distance = 3f; public float height = 1f; public float duration = 0.5f; }
    }
}
