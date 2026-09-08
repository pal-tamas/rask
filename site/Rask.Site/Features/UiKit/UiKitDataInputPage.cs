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
            "UI kit — Data input — Rask",
            "daisyUI's Data input components as typed Rask components: input, textarea, select, file "
            + "input, checkbox, toggle, radio, range, rating, fieldset, validator, label, floating "
            + "label, one-time code, filter, calendar and mask.",
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
            .Result(UiKitDataInputDemo)
    ];
}
