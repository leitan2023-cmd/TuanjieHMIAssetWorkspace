using System.Collections.Generic;
using UnityEngine;
using HMI.Workspace.Editor.Data;
using HMI.Workspace.Runtime.Logic;

namespace HMI.Workspace.Editor.Core
{
    public readonly struct FilteredAssetsChangedEvent
    {
        public FilteredAssetsChangedEvent(IReadOnlyList<AssetEntry> assets) => Assets = assets;
        public IReadOnlyList<AssetEntry> Assets { get; }
    }

    public readonly struct ThumbnailReadyEvent
    {
        public ThumbnailReadyEvent(string guid, Texture2D texture)
        {
            Guid = guid;
            Texture = texture;
        }
        public string Guid { get; }
        public Texture2D Texture { get; }
    }

    public readonly struct PreviewTextureReadyEvent
    {
        public PreviewTextureReadyEvent(Texture2D texture) => Texture = texture;
        public Texture2D Texture { get; }
    }

    public readonly struct ActionExecutedEvent
    {
        public ActionExecutedEvent(string actionName, string message)
        {
            ActionName = actionName;
            Message = message;
        }
        public string ActionName { get; }
        public string Message { get; }
    }

    public readonly struct ActionFailedEvent
    {
        public ActionFailedEvent(string actionName, string reason)
        {
            ActionName = actionName;
            Reason = reason;
        }
        public string ActionName { get; }
        public string Reason { get; }
    }

    public readonly struct ActionStatesChangedEvent { }

    public readonly struct ContextSuggestionsReadyEvent
    {
        public ContextSuggestionsReadyEvent(IReadOnlyList<string> suggestions) => Suggestions = suggestions;
        public IReadOnlyList<string> Suggestions { get; }
    }

    public readonly struct CommandResultReadyEvent
    {
        public CommandResultReadyEvent(string result) => Result = result;
        public string Result { get; }
    }

    public static class AssetEvents
    {
        public static readonly EventChannel<FilteredAssetsChangedEvent> FilteredChanged = new();
        public static readonly EventChannel<ThumbnailReadyEvent> ThumbnailReady = new();
    }

    public static class PreviewEvents
    {
        public static readonly EventChannel<PreviewTextureReadyEvent> TextureReady = new();
    }

    public static class ActionEvents
    {
        public static readonly EventChannel<ActionStatesChangedEvent> StatesChanged = new() { DebugLabel = "ActionEvents.StatesChanged" };
        public static readonly EventChannel<ActionExecutedEvent> Executed = new() { DebugLabel = "ActionEvents.Executed" };
        public static readonly EventChannel<ActionFailedEvent> Failed = new() { DebugLabel = "ActionEvents.Failed" };
    }

    public static class DependencyEvents
    {
        public static readonly EventChannel<AssetDepsResolvedEvent> AssetResolved = new();
    }

    public static class AIEvents
    {
        public static readonly EventChannel<ContextSuggestionsReadyEvent> SuggestionsReady = new();
        public static readonly EventChannel<CommandResultReadyEvent> CommandResult = new();
    }

    // ── 跨视图模式选择上下文事件 ──────────────────────────────────

    /// <summary>
    /// 选择上下文事件 — 由任意视图模式发布，InspectorPanelView 消费。
    /// 携带当前视图模式下选中的摘要信息，不依赖特定状态对象。
    /// </summary>
    public readonly struct SelectionContextEvent
    {
        public SelectionContextEvent(
            string sourceMode,
            string title,
            string subtitle,
            string detail,
            string actionHint,
            Texture2D preview = null)
        {
            SourceMode = sourceMode;
            Title = title;
            Subtitle = subtitle;
            Detail = detail;
            ActionHint = actionHint;
            Preview = preview;
        }

        /// <summary>来源模式标识：AssetBrowser, BatchReplace, SceneBuilder, VehicleSetup</summary>
        public string SourceMode { get; }
        /// <summary>主标题（如资产名、模板名、目标对象名）</summary>
        public string Title { get; }
        /// <summary>副标题（如类型、分类）</summary>
        public string Subtitle { get; }
        /// <summary>详细信息（多行，如路径、配置摘要）</summary>
        public string Detail { get; }
        /// <summary>操作提示（如 "选择候选材质以启用替换"）</summary>
        public string ActionHint { get; }
        /// <summary>预览图（可选）</summary>
        public Texture2D Preview { get; }
    }

    /// <summary>
    /// 选择上下文清除事件 — 当切换视图模式或取消选择时发布。
    /// </summary>
    public readonly struct SelectionContextClearedEvent
    {
        public SelectionContextClearedEvent(string sourceMode, string emptyMessage)
        {
            SourceMode = sourceMode;
            EmptyMessage = emptyMessage;
        }

        public string SourceMode { get; }
        public string EmptyMessage { get; }
    }

    public static class SelectionEvents
    {
        public static readonly EventChannel<SelectionContextEvent> ContextChanged = new();
        public static readonly EventChannel<SelectionContextClearedEvent> ContextCleared = new();
    }

    // ── Logic Flow 事件 ─────────────────────────────────────────

    public readonly struct LogicGraphLoadedEvent
    {
        public LogicGraphLoadedEvent(LogicGraph graph) => Graph = graph;
        public LogicGraph Graph { get; }
    }

    public readonly struct LogicGraphChangedEvent { }

    public readonly struct LogicExecutionCompletedEvent
    {
        public LogicExecutionCompletedEvent(string triggerName, bool success)
        {
            TriggerName = triggerName;
            Success = success;
        }
        public string TriggerName { get; }
        public bool Success { get; }
    }

    public static class LogicEvents
    {
        public static readonly EventChannel<LogicGraphLoadedEvent> GraphLoaded = new() { DebugLabel = "LogicEvents.GraphLoaded" };
        public static readonly EventChannel<LogicGraphChangedEvent> GraphChanged = new() { DebugLabel = "LogicEvents.GraphChanged" };
        public static readonly EventChannel<LogicExecutionCompletedEvent> ExecutionCompleted = new() { DebugLabel = "LogicEvents.ExecutionCompleted" };
    }

    /// <summary>
    /// 资产依赖解析完成事件。
    /// DependencyController 异步解析资产依赖后发布，DependencyPanelView 消费。
    /// </summary>
    public readonly struct AssetDepsResolvedEvent
    {
        public AssetDepsResolvedEvent(string guid, IReadOnlyList<string> dependencyPaths)
        {
            Guid = guid;
            DependencyPaths = dependencyPaths;
        }

        public string Guid { get; }
        public IReadOnlyList<string> DependencyPaths { get; }
    }
}
