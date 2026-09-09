using System.Reflection;
using Rask.Core.Components;

namespace Rask.Auth.Pages;

/// <summary>
/// The shared chrome for the built-in sign-in, registration and sign-out pages.
/// </summary>
/// <remarks>
/// <para>
/// The styles ride in a <c>&lt;style&gt;</c> block rather than a <c>&lt;link&gt;</c>, because these pages
/// ship inside a package and must render on an app that has no CSS of its own. The framework dedupes
/// head contributions by their rendered HTML, so the block is emitted once no matter how many of these
/// pages are in the tree.
/// </para>
/// <para>
/// What is in that block is daisyUI, compiled at <b>this package's</b> build from
/// <c>Styles/auth.css</c>. It has to be: Tailwind emits a class only where it can see the name, and
/// these class names live in a compiled assembly that no application's Tailwind can scan. Because the
/// only source scanned is the pages beside this file, the sheet is a few KB rather than the whole
/// library — which is what makes carrying it inline affordable.
/// </para>
/// <para>
/// The palette is scoped to <c>data-rask-auth</c> on the wrapper below, for the same reason the kit
/// scopes its own: referencing a package must not repaint the application. An app that wants these
/// pages in its own colours declares its own page at the same route, which takes precedence — see
/// <see cref="LoginPage" />.
/// </para>
/// </remarks>
public abstract partial class AuthPage : Component
{
    /// <summary>Marks the subtree daisyUI's palette applies to. Nothing outside it is repainted.</summary>
    private const string ThemeScope = "data-rask-auth";

    private static string? _css;

    /// <summary>
    /// The compiled stylesheet, read once from this assembly.
    /// </summary>
    /// <remarks>
    /// Empty rather than throwing when the resource is absent, so a trimmed or mis-packed build leaves
    /// the pages unstyled rather than unable to start. The build fails loudly instead — see
    /// <c>EmbedRaskAuthStylesheet</c> in the project file.
    /// </remarks>
    private static string Css => _css ??= Read();

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
        // The theme scope goes here because a component cannot reach the document element. Every colour
        // below resolves against it, so without the attribute the page renders structurally correct and
        // completely grey.
        Div
            .Attributes((ThemeScope, ""))
            .Class("hero min-h-screen bg-base-200")[
            Div.Class("hero-content w-full max-w-sm flex-col")[
                Div.Class("card bg-base-100 w-full shadow-sm")[
                    Div.Class("card-body gap-4")[Content]
                ]
            ]
        ];

    /// <summary>A labelled input, the shape all the pages use.</summary>
    /// <param name="id">The input's id, which the label points at.</param>
    /// <param name="label">The visible label.</param>
    /// <param name="input">The bound input, already opened with <c>Input.Bind(...)</c>.</param>
    /// <remarks>
    /// A real <c>&lt;label for&gt;</c> rather than a <c>&lt;legend&gt;</c>: the legend styles the same
    /// way in daisyUI but carries no association with the control, and that association is what makes
    /// the caption the field's accessible name and its click target.
    /// </remarks>
    protected static Component Field(string id, string label, Component input) =>
        Div.Class("fieldset")[Label.For(id).Class("fieldset-legend")[label], input];

    /// <summary>An error the whole form is reporting, as daisyUI's alert.</summary>
    /// <remarks>
    /// <c>role="alert"</c> so it is announced when it appears, and an icon would be redundant here —
    /// the text is the message.
    /// </remarks>
    protected static Component Error(string id, Component? message) =>
        Div.Class("alert alert-error").Role("alert").Id(id)[Span[message]];

    /// <summary>A success notice, in the same shape as <see cref="Error" />.</summary>
    protected static Component Ok(string id, Component? message) =>
        Div.Class("alert alert-success").Role("alert").Id(id)[Span[message]];

    private static string Read()
    {
        using var stream = typeof(AuthPage).Assembly.GetManifestResourceStream("Rask.Auth.auth.css");
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
