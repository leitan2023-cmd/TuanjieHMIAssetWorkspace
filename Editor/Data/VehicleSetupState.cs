using System.Collections.Generic;
using HMI.Workspace.Editor.Core;

namespace HMI.Workspace.Editor.Data
{
    /// <summary>
    /// 零件状态 — 用户工作流维度的分类。
    /// </summary>
    public enum PartStatus
    {
        /// <summary>类型已识别 + 命名规范 → 可直接使用</summary>
        Ready,
        /// <summary>类型已识别但命名不规范 → 建议修复</summary>
        NeedsFix,
        /// <summary>无法从名称推断类型（Object026 等）→ 需要用户手动绑定</summary>
        Unrecognized,
        /// <summary>用户主动忽略的条目</summary>
        Ignored,
    }

    /// <summary>
    /// 车辆零件数据模型。
    /// 由 VehicleSetupController 在导入 FBX/Prefab 后扫描子层级自动生成。
    /// </summary>
    public sealed class VehiclePart
    {
        public string Name { get; set; }
        public string ObjectPath { get; set; }
        public VehiclePartType PartType { get; set; }
        public string BoundGameObject { get; set; }
        public List<MaterialSlot> MaterialSlots { get; set; } = new();
        public bool NamingValid { get; set; }
        public string ValidationMessage { get; set; }

        /// <summary>工作流状态：Ready / NeedsFix / Unrecognized / Ignored</summary>
        public PartStatus Status { get; set; }

        /// <summary>是否为 DCC 工具生成的无意义名称（Object001 等）</summary>
        public bool IsMeaninglessName { get; set; }

        /// <summary>建议的标准命名（VP_{Type}_{Name}）</summary>
        public string SuggestedName { get; set; }
    }

    public sealed class MaterialSlot
    {
        public int Index { get; set; }
        public string MaterialName { get; set; }
        public string ShaderName { get; set; }
    }

    public enum VehiclePartType
    {
        Unknown,
        Body,
        Wheel,
        Light,
        Interior,
        Glass,
        Trim,
        Chassis,
    }

    /// <summary>
    /// 车辆设置工作区的完整响应式状态。
    /// 每个 Observable 变更时驱动对应 UI 区域刷新。
    /// </summary>
    public sealed class VehicleSetupState
    {
        public Observable<string> ImportPath { get; } = new("");
        public Observable<string> VehicleName { get; } = new("\u672A\u5BFC\u5165");
        public Observable<List<VehiclePart>> Parts { get; } = new(new List<VehiclePart>());
        public Observable<VehiclePart> SelectedPart { get; } = new();
        public Observable<int> TotalParts { get; } = new(0);
        public Observable<int> ValidParts { get; } = new(0);
        public Observable<string> ValidationSummary { get; } = new("\u7B49\u5F85\u5BFC\u5165\u8F66\u8F86\u6A21\u578B\u2026");
        public Observable<string> SchemaJson { get; } = new("");
    }
}
