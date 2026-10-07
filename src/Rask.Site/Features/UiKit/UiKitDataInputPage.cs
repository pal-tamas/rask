using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Data input components, live.
/// </summary>
[Route("ui/data-input")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataInputPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "daisyUI form inputs as typed C# components — Rask",
            "daisyUI data input components in C#, each with a required label: input, textarea, select, "
            + "checkbox, toggle, radio, range, rating, one-time code and calendar.",
            Routes.UiKitDataInputPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data input"],
        P.Class("text-ui-muted")[
            "Every control takes a required label, and it becomes the accessible name rather than a ",
            "placeholder — a placeholder disappears the moment typing starts, so the one thing saying ",
            "what a field is for vanishes exactly when a reader might check it, and it is invisible to ",
            "anybody reviewing a filled-in form. The whole daisyUI class API is here: every tone, every ",
            "size, and ", Code["ghost"], " on the three controls daisyUI defines it for."
        ],
        CodeSample
            .Files(["UiKitDataInputDemo.cs"])
            .Notes("The one-time code is a single input drawn as several — per-digit boxes need script "
                + "to move focus, defeat SMS autofill and drop a pasted code into the first box. The "
                + "calendar is a C# month grid, because the element daisyUI styles for it is a "
                + "JavaScript web component the kit does not ship.")
            .Result(UiKitDataInputDemo),
        H2.Class("text-xl font-semibold mt-10 mb-1")["Rich text editor"],
        P.Class("text-ui-muted")[
            "Flux UI's editor, example for example: a toolbar over an editable area whose value is HTML. ",
            "The engine is Tiptap, and it is the one script of the kit that is not on every page — the ",
            "browser fetches it the first time an editor mounts, from ", Code["wwwroot/js/rask-ui-editor.js"],
            ", which the build writes with nothing asked of the project."
        ],
        CodeSample
            .Files(["UiKitEditorDemo.cs"])
            .Notes("The value is HTML the reader typed: the demo shows it as text. Sanitize it before it is "
                + "rendered as markup anywhere, and before a value that did not come from this editor is bound to it.")
            .Result(UiKitEditorDemo.Key("editor"))
    ];
}
