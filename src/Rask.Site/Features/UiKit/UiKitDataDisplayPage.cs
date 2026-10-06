using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Data display components, live.
/// </summary>
[Route("ui/data-display")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataDisplayPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Badge, accordion and card components in C# — Rask",
            "Data display components in C#: a Flux UI badge in every Tailwind colour, accordion, collapse, card, "
            + "kbd, status, countdown, chat bubble, aura and hover effects.",
            Routes.UiKitDataDisplayPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data display"],
        P.Class("text-ui-muted")[
            "The badge is Flux UI's, example for example: ", Code["Ui.Badge.Color(Ui.Color.Lime)[\"New\"]"],
            ", pressed with ", Code[".As(Ui.BadgeAs.Button).OnClick(…)"], " and removable with a ",
            Code["Ui.BadgeClose"], " among its children. ",
            "The rest of this category is mostly static. The two that hold state — the accordion and the collapse ",
            "— hold it in C#: ", Code["Open"], " is nullable, so unset leaves the browser to open it on ",
            "focus and a value takes ownership. Closed writes ", Code["collapse-close"], " rather than ",
            "merely omitting ", Code["collapse-open"], ", because daisyUI also opens on ",
            Code[":focus-within"], "."
        ],
        CodeSample
            .Files(["UiKitDataDisplayDemo.cs"])
            .Notes("The accordion's open key and the collapse's flag are plain fields. Aura, hover 3D "
                + "and hover gallery are decoration — they carry no role and no label, because a reader "
                + "who cannot see them loses nothing.")
            .Result(UiKitDataDisplayDemo)
    ];
}
