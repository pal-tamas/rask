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
            "UI kit — Data display — Rask",
            "daisyUI's Data display components as typed Rask components: accordion, collapse, aura, "
            + "text rotate, hover 3D, hover gallery, badge, kbd, status, countdown and chat bubble.",
            Routes.UiKitDataDisplayPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data display"],
        P.Class("text-ui-muted")[
            "Most of this category is static. The two that hold state — the accordion and the collapse ",
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
