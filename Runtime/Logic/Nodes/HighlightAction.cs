using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 动作：高亮目标对象（修改材质颜色或添加描边效果）。
    /// HMI 演示中最常用的交互之一。
    /// </summary>
    public class HighlightAction : LogicNodeBase
    {
        public Color HighlightColor = new Color(1f, 0.8f, 0f, 1f); // 默认金黄色
        public bool UseEmission = true;

        public override NodeCategory Category => NodeCategory.Action;
        public override string DisplayName => "高亮";
        public override string Description => "高亮显示目标对象（通过 Emission 或颜色叠加）";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
            new PortDefinition("目标", PortDirection.Input, PortKind.Data, dataType: typeof(GameObject)),
            new PortDefinition("颜色", PortDirection.Input, PortKind.Data, dataType: typeof(Color))
        };

        public override string Execute(LogicExecutionContext context)
        {
            var target = TargetObject;
            var inputTarget = context.GetInputValue<GameObject>(Id, "目标");
            if (inputTarget != null) target = inputTarget;

            var color = context.GetInputValue(Id, "颜色", HighlightColor);

            if (target != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // 使用 MaterialPropertyBlock 避免修改共享材质
                    var mpb = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(mpb);

                    if (UseEmission)
                    {
                        mpb.SetColor("_EmissionColor", color);
                        // 尝试启用 Emission 关键字
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat != null) mat.EnableKeyword("_EMISSION");
                        }
                    }
                    else
                    {
                        mpb.SetColor("_BaseColor", color);
                    }

                    renderer.SetPropertyBlock(mpb);
                }
            }

            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { r = HighlightColor.r, g = HighlightColor.g, b = HighlightColor.b, a = HighlightColor.a, useEmission = UseEmission });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            HighlightColor = new Color(data.r, data.g, data.b, data.a);
            UseEmission = data.useEmission;
        }

        [System.Serializable]
        private class SerData { public float r = 1, g = 0.8f, b = 0, a = 1; public bool useEmission = true; }
    }
}
