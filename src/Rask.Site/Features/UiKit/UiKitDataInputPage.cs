using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

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
            "Flux UI pickers and rich inputs in C# — Rask",
            "Flux UI's composer, autocomplete, pillbox, toggle, phone, calendar, date, time and color pickers and file "
            + "upload as typed C# components.",
            Routes.UiKitDataInputPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data input"],
        P.Class("text-ui-muted")[
            "The kit's pickers and richer inputs, until each has a page of its own in the UI tab. The basic form ",
            "controls already do."
        ],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Field, UiDocsCatalog.Input, UiDocsCatalog.Textarea, UiDocsCatalog.Select, UiDocsCatalog.Checkbox,
            UiDocsCatalog.Radio, UiDocsCatalog.Switch, UiDocsCatalog.Slider, UiDocsCatalog.Otp,
        ]),
        CodeSample
            .Files(["UiKitDataInputDemo.cs", "UiKitDataInputDemo.Composer.cs", "UiKitColorPickerDemo.cs", "UiKitToggleDemo.cs"])
            .Notes("The calendar is a C# month grid, because the element daisyUI styles for it is a JavaScript web "
                + "component the kit does not ship.")
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
