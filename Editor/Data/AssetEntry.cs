using UnityEngine;

namespace HMI.Workspace.Editor.Data
{
    public sealed class AssetEntry
    {
        public string Guid { get; set; }
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public AssetKind Kind { get; set; }
        public Object UnityObject { get; set; }

        // ── Gap Analysis 5.1: spec + HTML prototype 要求的扩展字段 ──
        public string[] Tags { get; set; }
        public bool Favorite { get; set; }
        public string Status { get; set; }       // ready / processing / draft / approved
        public string FileSize { get; set; }
        public string ModifiedDate { get; set; }
        public string Category { get; set; }
        public string[] Labels { get; set; }

        // ── 真实资产源适配字段 ──

        /// <summary>来源标识：Assets / Material Library / Package 等</summary>
        public string SourceLabel { get; set; }

        /// <summary>材质的 Shader 名称（仅 Material 类型有值），用于技术依赖展示</summary>
        public string ShaderName { get; set; }

        public override string ToString() => DisplayName ?? Guid ?? base.ToString();
    }
}
