namespace Rask.External;

/// <summary>
///     What a children indexer on an island returns when the children are not ones the island can render — Rask markup,
///     another runtime's island, or anything at all for an island whose component takes no content.
/// </summary>
/// <remarks>
///     A <c>ref struct</c>, so it cannot become a <see cref="Rask.Core.Component" />, cannot be boxed into an
///     <c>object?[]</c> children list, and so can never reach a render: the mistake stays a compile error. RASK062
///     reports the call at its brackets, because the compiler's own error arrives only where this result fails to
///     convert — and never for a <c>var</c> that holds it.
/// </remarks>
public readonly ref struct NotAChildOfThisIsland;
