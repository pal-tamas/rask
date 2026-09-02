using Rask.Core.Components;

namespace Rask.Auth.Pages;

/// <summary>
/// The shared chrome for the built-in sign-in, registration and sign-out pages.
/// </summary>
/// <remarks>
/// <para>
/// The styles ride in a <c>&lt;style&gt;</c> block rather than a scoped stylesheet or a Tailwind build,
/// because these pages ship inside a package and must render on an app that has no CSS of its own.
/// The framework dedupes head contributions by their rendered HTML, so the block is emitted once no
/// matter how many of these pages are in the tree.
/// </para>
/// <para>
/// Everything is deliberately plain: system fonts, <c>color-scheme</c> for the light/dark split, and
/// no colour that assumes a brand. An app that wants its own look declares its own page at the same
/// route, which takes precedence — see <see cref="LoginPage" />.
/// </para>
/// </remarks>
public abstract partial class AuthPage : Component
{
    // Scoped by the wrapper class rather than by a generated hash, so it cannot leak into an app's
    // own markup and needs no build step to travel inside the package.
    private const string Css = """
        .rask-auth{color-scheme:light dark;display:flex;justify-content:center;padding:3rem 1rem;
        font:16px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
        .rask-auth-card{width:100%;max-width:22rem}
        .rask-auth h1{font-size:1.375rem;margin:0 0 1.25rem}
        .rask-auth label{display:block;font-size:.875rem;font-weight:500;margin:0 0 .35rem}
        .rask-auth .rask-auth-field{margin:0 0 1rem}
        .rask-auth input{width:100%;box-sizing:border-box;padding:.5rem .625rem;font:inherit;
        font-size:.9375rem;border:1px solid rgba(128,128,128,.45);border-radius:.375rem;
        background:Canvas;color:CanvasText}
        .rask-auth input:focus-visible{outline:2px solid Highlight;outline-offset:1px;
        border-color:Highlight}
        .rask-auth button{width:100%;padding:.55rem .75rem;font:inherit;font-size:.9375rem;
        font-weight:500;border:1px solid transparent;border-radius:.375rem;cursor:pointer;
        background:Highlight;color:HighlightText}
        .rask-auth button:hover{filter:brightness(1.08)}
        .rask-auth .rask-auth-error{margin:0 0 1rem;padding:.55rem .75rem;font-size:.875rem;
        border-radius:.375rem;border:1px solid rgba(180,40,40,.45);background:rgba(180,40,40,.12)}
        .rask-auth .rask-auth-note{margin:1rem 0 0;font-size:.8125rem;opacity:.75}
        .rask-auth a{color:inherit}
        """;

    /// <inheritdoc />
    protected override Component? HeadAssets => Style[Raw.Value(Css)];

    /// <summary>
    /// The page's content, placed inside the shared card.
    /// </summary>
    /// <remarks>
    /// Named <c>Content</c> rather than <c>Body</c>: inside a markup host <c>Body</c> is the chain
    /// entry for the <c>&lt;body&gt;</c> element, and a member of that name hides it.
    /// </remarks>
    protected abstract Component? Content { get; }

    /// <inheritdoc />
    protected sealed override Component? Render() =>
        Div.Class("rask-auth")[Div.Class("rask-auth-card")[Content]];

    /// <summary>A labelled input, the shape all three pages use.</summary>
    /// <param name="id">The input's id, which the label points at.</param>
    /// <param name="label">The visible label.</param>
    /// <param name="input">The bound input, already opened with <c>Input.Bind(...)</c>.</param>
    protected static Component Field(string id, string label, Component input) =>
        Div.Class("rask-auth-field")[Label.For(id)[label], input];
}
