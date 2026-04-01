using System.Collections.Generic;
using UnityEngine;
using HMI.Workspace.Editor.Data;

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
        public static readonly EventChannel<ActionStatesChangedEvent> StatesChanged = new();
        public static readonly EventChannel<ActionExecutedEvent> Executed = new();
        public static readonly EventChannel<ActionFailedEvent> Failed = new();
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
