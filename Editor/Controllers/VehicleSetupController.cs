using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using HMI.Workspace.Editor.Core;
using HMI.Workspace.Editor.Data;

namespace HMI.Workspace.Editor.Controllers
{
    /// <summary>
    /// 车辆配置控制器 — VehicleSetup 工作区的真实控制入口。
    ///
    /// 职责：
    /// 1. 导入车辆模型并扫描零件层级
    /// 2. 自动检测零件类型、验证命名规范
    /// 3. 为每个零件计算工作流状态（Ready / NeedsFix / Unrecognized / Ignored）
    /// 4. 提供修复动作：自动修复命名、绑定类型、忽略
    /// 5. 导出车辆 Schema JSON
    /// 6. 发布上下文事件，驱动 InspectorPanel 联动
    /// </summary>
    public sealed class VehicleSetupController : IController
    {
        private readonly WorkspaceState _state;
        private readonly VehicleSetupState _vs = new();
        private GameObject _importedPrefab;

        /// <summary>VehicleSetup 状态，供 View 绑定。</summary>
        public VehicleSetupState SetupState => _vs;

        /// <summary>已导入的 Prefab 引用，供 View 用于 3D 预览。</summary>
        public GameObject ImportedPrefab => _importedPrefab;

        public VehicleSetupController(WorkspaceState state)
        {
            _state = state;
        }

        public void Initialize()
        {
            // 零件选中变化 → 发布上下文
            _vs.SelectedPart.Changed += (_, part) => PublishPartContext(part);
        }

        // ════════════════════════════════════════════════════════════
        // 导入 & 扫描
        // ════════════════════════════════════════════════════════════

        /// <summary>导入车辆模型，返回是否成功。</summary>
        public bool ImportVehicle(string assetPath)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj == null)
            {
                _state.StatusMessage.Value = $"\u65E0\u6CD5\u52A0\u8F7D\uFF1A{assetPath}";
                ActionEvents.Failed.Publish(new ActionFailedEvent("VehicleImport", $"\u65E0\u6CD5\u52A0\u8F7D\uFF1A{assetPath}"));
                return false;
            }

            _vs.ImportPath.Value = assetPath;
            _vs.VehicleName.Value = obj.name;
            _importedPrefab = obj as GameObject;

            ScanParts(_importedPrefab);

            _state.StatusMessage.Value = $"\u5DF2\u5BFC\u5165\u8F66\u8F86\u6A21\u578B\uFF1A{obj.name}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("VehicleImport",
                $"\u5DF2\u5BFC\u5165 {obj.name}\uFF0C\u68C0\u6D4B\u5230 {_vs.Parts.Value.Count} \u4E2A\u96F6\u4EF6"));

            PublishVehicleContext();
            return true;
        }

        private void ScanParts(GameObject prefab)
        {
            var parts = new List<VehiclePart>();
            if (prefab != null)
                ScanRecursive(prefab.transform, "", parts);

            _vs.Parts.Value = parts;
            _vs.TotalParts.Value = parts.Count;

            ValidateAllParts();
        }

        private void ScanRecursive(Transform t, string parentPath, List<VehiclePart> parts)
        {
            var path = string.IsNullOrEmpty(parentPath) ? t.name : $"{parentPath}/{t.name}";

            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                var meaningless = CheckMeaninglessName(t.name);
                var partType = meaningless ? VehiclePartType.Unknown : DetectPartType(t.name);

                var part = new VehiclePart
                {
                    Name = t.name,
                    ObjectPath = path,
                    PartType = partType,
                    BoundGameObject = t.name,
                    IsMeaninglessName = meaningless,
                };

                // 提取材质槽
                if (renderer.sharedMaterials != null)
                {
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var mat = renderer.sharedMaterials[i];
                        part.MaterialSlots.Add(new MaterialSlot
                        {
                            Index = i,
                            MaterialName = mat != null ? mat.name : "(\u7A7A)",
                            ShaderName = mat != null ? mat.shader.name : "N/A",
                        });
                    }
                }

                parts.Add(part);
            }

            for (int i = 0; i < t.childCount; i++)
                ScanRecursive(t.GetChild(i), path, parts);
        }

        // ════════════════════════════════════════════════════════════
        // 零件识别 & 验证
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 检查是否为 DCC 工具自动生成的无意义名称。
        /// Object001, Node_003, Group1, 纯数字等。
        /// </summary>
        public static bool CheckMeaninglessName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            var lower = name.ToLowerInvariant().Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(lower,
                @"^(object|node|group|null|dummy|bone|joint|locator|helper|mesh|polysurface|pasted)[\s_\-]?\d*$"))
                return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^\d+$"))
                return true;
            return false;
        }

        /// <summary>从名称关键词推断零件类型。</summary>
        public static VehiclePartType DetectPartType(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("body") || lower.Contains("shell") || lower.Contains("frame")) return VehiclePartType.Body;
            if (lower.Contains("wheel") || lower.Contains("tire") || lower.Contains("rim")) return VehiclePartType.Wheel;
            if (lower.Contains("light") || lower.Contains("lamp") || lower.Contains("headlight") || lower.Contains("taillight")) return VehiclePartType.Light;
            if (lower.Contains("interior") || lower.Contains("seat") || lower.Contains("dashboard") || lower.Contains("steering")) return VehiclePartType.Interior;
            if (lower.Contains("glass") || lower.Contains("window") || lower.Contains("windshield")) return VehiclePartType.Glass;
            if (lower.Contains("trim") || lower.Contains("mirror") || lower.Contains("handle") || lower.Contains("bumper")) return VehiclePartType.Trim;
            if (lower.Contains("chassis") || lower.Contains("axle") || lower.Contains("suspension")) return VehiclePartType.Chassis;
            return VehiclePartType.Unknown;
        }

        private void ValidateAllParts()
        {
            int valid = 0;
            foreach (var part in _vs.Parts.Value)
            {
                // 命名规范：VP_{Type}_{Name}
                part.NamingValid = part.Name.StartsWith("VP_") || part.Name.StartsWith("vp_");
                part.SuggestedName = part.PartType != VehiclePartType.Unknown
                    ? $"VP_{part.PartType}_{SanitizeName(part.Name)}"
                    : null;

                // 工作流状态
                if (part.Status == PartStatus.Ignored)
                {
                    // 用户已忽略，不改变
                    part.ValidationMessage = "\u2014 \u5DF2\u5FFD\u7565";
                }
                else if (part.NamingValid)
                {
                    part.Status = PartStatus.Ready;
                    part.ValidationMessage = "\u2705 \u547D\u540D\u7B26\u5408\u89C4\u8303";
                    valid++;
                }
                else if (part.IsMeaninglessName)
                {
                    part.Status = PartStatus.Unrecognized;
                    part.ValidationMessage = "\u9700\u8981\u624B\u52A8\u7ED1\u5B9A\u7C7B\u578B";
                }
                else if (part.PartType != VehiclePartType.Unknown)
                {
                    part.Status = PartStatus.NeedsFix;
                    part.ValidationMessage = $"\u5EFA\u8BAE\u91CD\u547D\u540D\u4E3A {part.SuggestedName}";
                    // 类型已识别算部分有效
                }
                else
                {
                    part.Status = PartStatus.Unrecognized;
                    part.ValidationMessage = "\u65E0\u6CD5\u8BC6\u522B\u7C7B\u578B\uFF0C\u8BF7\u624B\u52A8\u7ED1\u5B9A";
                }
            }

            _vs.ValidParts.Value = valid;
            var total = _vs.Parts.Value.Count;
            var needsFix = _vs.Parts.Value.Count(p => p.Status == PartStatus.NeedsFix);
            var unrecognized = _vs.Parts.Value.Count(p => p.Status == PartStatus.Unrecognized);
            var ignored = _vs.Parts.Value.Count(p => p.Status == PartStatus.Ignored);

            if (total == 0)
            {
                _vs.ValidationSummary.Value = "\u7B49\u5F85\u5BFC\u5165\u8F66\u8F86\u6A21\u578B\u2026";
            }
            else if (needsFix == 0 && unrecognized == 0)
            {
                _vs.ValidationSummary.Value = $"\u2705 \u5168\u90E8 {valid} \u4E2A\u96F6\u4EF6\u5C31\u7EEA\uFF0C\u53EF\u76F4\u63A5\u5BFC\u51FA";
            }
            else
            {
                var parts = new List<string>();
                if (valid > 0) parts.Add($"{valid} \u4E2A\u5DF2\u5C31\u7EEA");
                if (needsFix > 0) parts.Add($"{needsFix} \u4E2A\u5EFA\u8BAE\u4FEE\u590D");
                if (unrecognized > 0) parts.Add($"{unrecognized} \u4E2A\u672A\u8BC6\u522B");
                if (ignored > 0) parts.Add($"{ignored} \u4E2A\u5DF2\u5FFD\u7565");
                _vs.ValidationSummary.Value = string.Join("\uFF0C", parts);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 修复动作
        // ════════════════════════════════════════════════════════════

        /// <summary>选中零件。</summary>
        public void SelectPart(VehiclePart part)
        {
            _vs.SelectedPart.Value = part;
        }

        /// <summary>自动修复单个零件命名。</summary>
        public bool AutoFixName(VehiclePart part)
        {
            if (part == null || part.PartType == VehiclePartType.Unknown)
                return false;

            var newName = $"VP_{part.PartType}_{SanitizeName(part.Name)}";
            part.Name = newName;
            part.NamingValid = true;
            part.Status = PartStatus.Ready;
            part.ValidationMessage = "\u2705 \u547D\u540D\u7B26\u5408\u89C4\u8303";
            part.SuggestedName = null;

            ValidateAllParts();
            _vs.Parts.Value = new List<VehiclePart>(_vs.Parts.Value); // 触发 Changed

            _state.StatusMessage.Value = $"\u5DF2\u4FEE\u590D\uFF1A{newName}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("AutoFixName", $"\u5DF2\u5C06\u96F6\u4EF6\u91CD\u547D\u540D\u4E3A {newName}"));
            PublishPartContext(part);
            return true;
        }

        /// <summary>批量自动修复所有可修复的零件。</summary>
        public int AutoFixAll()
        {
            int count = 0;
            foreach (var part in _vs.Parts.Value)
            {
                if (part.Status == PartStatus.NeedsFix && part.PartType != VehiclePartType.Unknown)
                {
                    var newName = $"VP_{part.PartType}_{SanitizeName(part.Name)}";
                    part.Name = newName;
                    part.NamingValid = true;
                    part.Status = PartStatus.Ready;
                    part.ValidationMessage = "\u2705 \u547D\u540D\u7B26\u5408\u89C4\u8303";
                    part.SuggestedName = null;
                    count++;
                }
            }

            if (count > 0)
            {
                ValidateAllParts();
                _vs.Parts.Value = new List<VehiclePart>(_vs.Parts.Value);
                _state.StatusMessage.Value = $"\u5DF2\u6279\u91CF\u4FEE\u590D {count} \u4E2A\u96F6\u4EF6\u547D\u540D";
                ActionEvents.Executed.Publish(new ActionExecutedEvent("AutoFixAll", $"\u5DF2\u6279\u91CF\u4FEE\u590D {count} \u4E2A\u96F6\u4EF6"));
                PublishVehicleContext();
            }

            return count;
        }

        /// <summary>手动绑定零件类型。</summary>
        public void BindPartType(VehiclePart part, VehiclePartType newType)
        {
            if (part == null) return;

            part.PartType = newType;
            part.IsMeaninglessName = false; // 用户已明确指定类型
            part.SuggestedName = $"VP_{newType}_{SanitizeName(part.Name)}";

            ValidateAllParts();
            _vs.Parts.Value = new List<VehiclePart>(_vs.Parts.Value);

            _state.StatusMessage.Value = $"\u5DF2\u5C06 {part.Name} \u7ED1\u5B9A\u4E3A {PartTypeToLabel(newType)}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("BindPartType",
                $"\u5DF2\u5C06 {part.Name} \u7ED1\u5B9A\u4E3A {PartTypeToLabel(newType)}"));
            PublishPartContext(part);
        }

        /// <summary>忽略零件问题。</summary>
        public void IgnoreIssue(VehiclePart part)
        {
            if (part == null) return;

            part.Status = PartStatus.Ignored;
            part.ValidationMessage = "\u2014 \u5DF2\u5FFD\u7565";

            ValidateAllParts();
            _vs.Parts.Value = new List<VehiclePart>(_vs.Parts.Value);

            _state.StatusMessage.Value = $"\u5DF2\u5FFD\u7565 {part.Name}";
            PublishPartContext(part);
        }

        /// <summary>将已忽略的零件恢复参与（重新进入验证流程）。</summary>
        public void RestoreFromIgnore(VehiclePart part)
        {
            if (part == null || part.Status != PartStatus.Ignored) return;

            // 清除 Ignored 标记，让 ValidateAllParts 重新判定状态
            part.Status = PartStatus.Unrecognized; // 临时值，ValidateAllParts 会重算

            ValidateAllParts();
            _vs.Parts.Value = new List<VehiclePart>(_vs.Parts.Value);

            _state.StatusMessage.Value = $"\u5DF2\u6062\u590D {part.Name} \u53C2\u4E0E\u914D\u7F6E";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("RestoreFromIgnore",
                $"\u5DF2\u6062\u590D {part.Name} \u53C2\u4E0E\u914D\u7F6E\uFF0C\u5F53\u524D\u72B6\u6001\uFF1A{StatusToLabel(part.Status)}"));
            PublishPartContext(part);
        }

        // ════════════════════════════════════════════════════════════
        // Schema 导出
        // ════════════════════════════════════════════════════════════

        /// <summary>生成 Schema JSON 字符串。</summary>
        public string GenerateSchemaJson()
        {
            var parts = _vs.Parts.Value;
            if (parts == null || parts.Count == 0) return "{}";

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"vehicleName\": \"{_vs.VehicleName.Value}\",");
            sb.AppendLine($"  \"totalParts\": {parts.Count},");
            sb.AppendLine("  \"parts\": [");

            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Status == PartStatus.Ignored) continue;
                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": \"{p.Name}\",");
                sb.AppendLine($"      \"type\": \"{p.PartType}\",");
                sb.AppendLine($"      \"status\": \"{p.Status}\",");
                sb.AppendLine($"      \"path\": \"{p.ObjectPath}\",");
                sb.AppendLine($"      \"namingValid\": {p.NamingValid.ToString().ToLowerInvariant()},");
                sb.AppendLine($"      \"materialSlots\": {p.MaterialSlots.Count}");
                sb.Append("    }");
                if (i < parts.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>导出 Schema 到文件。</summary>
        public bool ExportSchema()
        {
            var json = GenerateSchemaJson();
            _vs.SchemaJson.Value = json;

            var savePath = EditorUtility.SaveFilePanel(
                "\u4FDD\u5B58\u8F66\u8F86 Schema", "Assets",
                $"{_vs.VehicleName.Value}_schema", "json");
            if (string.IsNullOrEmpty(savePath)) return false;

            System.IO.File.WriteAllText(savePath, json);
            _state.StatusMessage.Value = $"Schema \u5DF2\u5BFC\u51FA\uFF1A{savePath}";
            ActionEvents.Executed.Publish(new ActionExecutedEvent("ExportSchema",
                $"\u5DF2\u5BFC\u51FA {_vs.VehicleName.Value} \u7684 Schema"));
            return true;
        }

        // ════════════════════════════════════════════════════════════
        // 上下文发布 — 驱动 InspectorPanel
        // ════════════════════════════════════════════════════════════

        public void PublishPartContext(VehiclePart part)
        {
            if (part == null)
            {
                SelectionEvents.ContextCleared.Publish(new SelectionContextClearedEvent(
                    "VehicleSetup", "\u4ECE\u5DE6\u4FA7\u96F6\u4EF6\u5217\u8868\u9009\u62E9\u4E00\u4E2A\u96F6\u4EF6"));
                return;
            }

            var statusLabel = StatusToLabel(part.Status);
            var detail = $"\u8DEF\u5F84\uFF1A{part.ObjectPath}";
            detail += $"\n\u7C7B\u578B\uFF1A{PartTypeToLabel(part.PartType)}";
            detail += $"\n\u72B6\u6001\uFF1A{statusLabel}";
            detail += $"\n\u547D\u540D\uFF1A{(part.NamingValid ? "\u2713 \u89C4\u8303" : part.ValidationMessage)}";
            detail += $"\n\u6750\u8D28\u69FD\uFF1A{part.MaterialSlots.Count} \u4E2A";
            if (part.MaterialSlots.Count > 0)
            {
                foreach (var slot in part.MaterialSlots)
                    detail += $"\n  [{slot.Index}] {slot.MaterialName}\uFF08{slot.ShaderName}\uFF09";
            }

            var actionHint = part.Status switch
            {
                PartStatus.Ready => "\u96F6\u4EF6\u5DF2\u5C31\u7EEA\uFF0C\u53EF\u76F4\u63A5\u5BFC\u51FA",
                PartStatus.NeedsFix => $"\u5EFA\u8BAE\u70B9\u51FB\u300C\u81EA\u52A8\u4FEE\u590D\u300D\u91CD\u547D\u540D\u4E3A {part.SuggestedName}",
                PartStatus.Unrecognized => "\u8BF7\u4ECE\u7C7B\u578B\u4E0B\u62C9\u83DC\u5355\u7ED1\u5B9A\u6B63\u786E\u7C7B\u578B",
                PartStatus.Ignored => "\u5DF2\u5FFD\u7565\u6B64\u96F6\u4EF6\uFF0C\u4E0D\u4F1A\u5BFC\u51FA\u5230 Schema",
                _ => "",
            };

            SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                "VehicleSetup",
                part.Name,
                $"{PartTypeToLabel(part.PartType)}  \u2022  {statusLabel}",
                detail,
                actionHint));
        }

        public void PublishVehicleContext()
        {
            var parts = _vs.Parts.Value;
            int total = parts.Count;
            int ready = parts.Count(p => p.Status == PartStatus.Ready);
            int needsFix = parts.Count(p => p.Status == PartStatus.NeedsFix);
            int unrecognized = parts.Count(p => p.Status == PartStatus.Unrecognized);

            var detail = $"\u8F66\u8F86\uFF1A{_vs.VehicleName.Value}";
            detail += $"\n\u96F6\u4EF6\u603B\u6570\uFF1A{total}";
            detail += $"\n\u5DF2\u5C31\u7EEA\uFF1A{ready}";
            if (needsFix > 0) detail += $"\n\u5EFA\u8BAE\u4FEE\u590D\uFF1A{needsFix}";
            if (unrecognized > 0) detail += $"\n\u672A\u8BC6\u522B\uFF1A{unrecognized}";

            var typeGroups = parts.Where(p => p.Status != PartStatus.Ignored)
                .GroupBy(p => p.PartType);
            foreach (var g in typeGroups)
                detail += $"\n  {PartTypeToLabel(g.Key)}\uFF1A{g.Count()} \u4E2A";

            SelectionEvents.ContextChanged.Publish(new SelectionContextEvent(
                "VehicleSetup",
                _vs.VehicleName.Value,
                $"{total} \u4E2A\u96F6\u4EF6  \u2022  {ready} \u4E2A\u5DF2\u5C31\u7EEA",
                detail,
                needsFix > 0 || unrecognized > 0
                    ? $"\u70B9\u51FB\u300C\u5168\u90E8\u81EA\u52A8\u4FEE\u590D\u300D\u53EF\u6279\u91CF\u4FEE\u590D {needsFix} \u4E2A\u96F6\u4EF6"
                    : "\u6240\u6709\u96F6\u4EF6\u5DF2\u5C31\u7EEA\uFF0C\u53EF\u5BFC\u51FA Schema"));
        }

        /// <summary>获取当前分组统计信息。</summary>
        public (int ready, int needsFix, int unrecognized, int ignored) GetStatusCounts()
        {
            var parts = _vs.Parts.Value;
            return (
                parts.Count(p => p.Status == PartStatus.Ready),
                parts.Count(p => p.Status == PartStatus.NeedsFix),
                parts.Count(p => p.Status == PartStatus.Unrecognized),
                parts.Count(p => p.Status == PartStatus.Ignored)
            );
        }

        // ════════════════════════════════════════════════════════════
        // 名称映射
        // ════════════════════════════════════════════════════════════

        public static string PartTypeToLabel(VehiclePartType type)
        {
            return type switch
            {
                VehiclePartType.Body     => "\u8F66\u8EAB",
                VehiclePartType.Wheel    => "\u8F66\u8F6E",
                VehiclePartType.Light    => "\u706F\u5149",
                VehiclePartType.Interior => "\u5185\u9970",
                VehiclePartType.Glass    => "\u73BB\u7483",
                VehiclePartType.Trim     => "\u88C5\u9970\u4EF6",
                VehiclePartType.Chassis  => "\u5E95\u76D8",
                _                        => "\u672A\u5206\u7C7B",
            };
        }

        public static string StatusToLabel(PartStatus status)
        {
            return status switch
            {
                PartStatus.Ready        => "\u2713 \u5DF2\u5C31\u7EEA",
                PartStatus.NeedsFix     => "\u26A0 \u5EFA\u8BAE\u4FEE\u590D",
                PartStatus.Unrecognized => "? \u672A\u8BC6\u522B",
                PartStatus.Ignored      => "\u2014 \u5DF2\u5FFD\u7565",
                _                       => "",
            };
        }

        private static string SanitizeName(string name)
        {
            // 移除已有前缀，清理特殊字符
            if (name.StartsWith("VP_") || name.StartsWith("vp_"))
                name = name.Substring(3);
            // 移除常见前缀
            foreach (var prefix in new[] { "Body_", "Wheel_", "Light_", "Interior_", "Glass_", "Trim_", "Chassis_" })
            {
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }
            return name.Replace(" ", "_").Replace("-", "_");
        }

        public void Dispose() { }
    }
}
