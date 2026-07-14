namespace Rask.Example.Playground.Compiler;

/// <summary>
///     One IntelliSense suggestion produced by <see cref="PlaygroundWorkspace.CompleteAsync" /> from Roslyn's
///     <c>CompletionService</c>, in the flat shape the Monaco completion provider consumes. <see cref="Kind" />
///     is Roslyn's primary well-known tag (e.g. <c>Method</c>, <c>Property</c>, <c>Keyword</c>); the editor JS
///     maps it onto a <c>monaco.languages.CompletionItemKind</c> icon.
/// </summary>
public sealed record PlaygroundCompletion(
    string Label,
    string Kind,
    string InsertText,
    string SortText,
    string? Detail);
