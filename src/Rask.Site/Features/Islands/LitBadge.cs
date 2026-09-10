namespace Rask.Site.Features.Islands;

/// <summary>
///     A badge rendered by <c>LitBadge.ts</c> — a plain custom element used as an ordinary Rask
///     component.
/// </summary>
/// <remarks>
///     <para>
///         The runtime that could not be shown here until #938. A Lit island is declared by putting
///         <c>LitBadge.ts</c> beside this file, which is character-for-character how Rask's scoped
///         TypeScript is declared too — and this app genuinely has both, so before the two were
///         separated by reading the C# base class every scoped file on the site was also offered to the
///         bundler as a Lit module that never default-exported a tag name.
///     </para>
///     <para>
///         Its front-end file imports NOTHING: a custom element needs no framework and no npm package,
///         which is the case the whole no-dependency half of the islands story exists for. It still
///         gets the same generated props and the same build-time type-check as a React island.
///     </para>
///     <para>
///         Keeps a count of its own that C# never sees, so it makes the same reconcile-not-remount
///         claim <see cref="SvelteMeter" /> does — for a runtime with no reconciler at all: the adapter
///         assigns properties onto a live element rather than tearing it down.
///     </para>
/// </remarks>
public sealed partial class LitBadge : Rask.External.LitComponent
{
    /// <summary>The caption C# owns.</summary>
    public required string Label { get; set; }

    /// <summary>The reading C# owns, re-rendered by the element on assignment.</summary>
    public int Value { get; set; }

    /// <summary>Runs with the element's own nudge count whenever it changes.</summary>
    public Action<int>? OnNudged { get; set; }
}
