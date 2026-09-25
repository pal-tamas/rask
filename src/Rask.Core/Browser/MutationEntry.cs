namespace Rask.Core.Browser;

/// <summary>One DOM-mutation notification for an observed element.</summary>
/// <param name="Type">What changed: <c>"childList"</c>, <c>"attributes"</c>, or <c>"characterData"</c>.</param>
/// <param name="AddedCount">Number of nodes added in this record (<c>addedNodes.length</c>).</param>
/// <param name="RemovedCount">Number of nodes removed in this record (<c>removedNodes.length</c>).</param>
/// <param name="AttributeName">The changed attribute's name, for an <c>"attributes"</c> record; otherwise <c>null</c>.</param>
public sealed record MutationEntry(string Type, int AddedCount, int RemovedCount, string? AttributeName);
