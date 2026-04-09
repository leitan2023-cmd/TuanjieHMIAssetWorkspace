using System.Collections.Generic;
using UnityEngine;

namespace HMI.Workspace.Runtime.Logic.Nodes
{
    /// <summary>
    /// 动作：替换目标对象的材质。
    /// 与现有 ActionController 的材质替换能力互补。
    /// </summary>
    public class SetMaterialAction : LogicNodeBase
    {
        public string MaterialPath; // 材质资产路径
        public int SlotIndex = -1;  // -1 表示替换全部槽位

        public override NodeCategory Category => NodeCategory.Action;
        public override string DisplayName => "设置材质";
        public override string Description => "替换目标对象的材质（可指定槽位）";

        public override List<PortDefinition> GetPorts() => new()
        {
            new PortDefinition("执行", PortDirection.Input, PortKind.Flow),
            new PortDefinition("完成", PortDirection.Output, PortKind.Flow),
            new PortDefinition("目标", PortDirection.Input, PortKind.Data, dataType: typeof(GameObject)),
            new PortDefinition("材质", PortDirection.Input, PortKind.Data, dataType: typeof(Material))
        };

        public override string Execute(LogicExecutionContext context)
        {
            var target = TargetObject;
            var inputTarget = context.GetInputValue<GameObject>(Id, "目标");
            if (inputTarget != null) target = inputTarget;

            var mat = context.GetInputValue<Material>(Id, "材质");

            if (target != null && mat != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (SlotIndex < 0)
                    {
                        // 替换所有槽位
                        var mats = renderer.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        renderer.sharedMaterials = mats;
                    }
                    else
                    {
                        var mats = renderer.sharedMaterials;
                        if (SlotIndex < mats.Length)
                        {
                            mats[SlotIndex] = mat;
                            renderer.sharedMaterials = mats;
                        }
                    }
                }
            }

            return "完成";
        }

        public override string Serialize() =>
            JsonUtility.ToJson(new SerData { materialPath = MaterialPath, slotIndex = SlotIndex });

        public override void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            var data = JsonUtility.FromJson<SerData>(json);
            MaterialPath = data.materialPath;
            SlotIndex = data.slotIndex;
        }

        [System.Serializable]
        private class SerData { public string materialPath; public int slotIndex = -1; }
    }
}
