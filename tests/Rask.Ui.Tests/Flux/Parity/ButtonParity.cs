using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/button</c>, example for example.
/// </summary>
/// <remarks>
///     One of that page's fifteen examples is not here: "As an input" is <c>flux:input as="button"</c>, the
///     input component drawn as a button, and carries no <c>data-flux-button</c>. It belongs to the input's page.
/// </remarks>
public sealed partial class ButtonParity : FluxParity
{
    // `w-full` on Flux's example is the APP's utility, so it is stated here under a name of this page's own.
    private const string AppUtilities = "<style>.parity-full{width:100%}</style>";

    private static readonly Ui.Color[] Hues =
    [
        Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    public override string Page => "button";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div[Raw.Value(AppUtilities), Ui.Button["Button"]]);

        yield return ("variants", Row(
            Ui.Button["Default"],
            Ui.Button.Primary["Primary"],
            Ui.Button.Filled["Filled"],
            Ui.Button.Danger["Danger"],
            Ui.Button.Ghost["Ghost"],
            Ui.Button.Subtle["Subtle"]));

        // Flux's live example shows the blue row its markdown gives and a red one under it.
        yield return ("colors", Stack(Swatches(Ui.Color.Blue), Swatches(Ui.Color.Red)));

        // All seventeen hues on the live page, where the markdown lists three.
        yield return ("outline-colors", Div.Style("display:flex;flex-wrap:wrap;gap:12px;max-width:560px")[
            Hues.Select(hue => Ui.Button.Color(hue).Key(hue)[hue.ToString()])
        ]);

        yield return ("sizes", Row(
            Ui.Button["Default"],
            Ui.Button.Sm["Small"],
            Ui.Button.Xs["Extra small"]));

        yield return ("icons", Row(
            Ui.Button.Icon(Ui.IconName.EllipsisHorizontal),
            Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"],
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Open"],
            Ui.Button.Subtle.Icon(Ui.IconName.XMark)));

        // `wire:click="save"`, which Flux's docs hold in its loading state.
        yield return ("loading", Div[Ui.Button.Loading(true)["Save changes"]]);

        yield return ("full-width", Div.Style("width:384px")[Ui.Button.Primary.Class("parity-full")["Send invite"]]);

        // Each group in a block of its own, as on Flux's page: a flex item computes another min-width.
        yield return ("button-groups", Row(Div[Ui.ButtonGroup[
            Ui.Button["Oldest"],
            Ui.Button["Newest"],
            Ui.Button["Top"]
        ]]));

        yield return ("icon-group", Row(Div[Ui.ButtonGroup[
            Ui.Button.Icon(Ui.IconName.Bars3BottomLeft),
            Ui.Button.Icon(Ui.IconName.Bars3),
            Ui.Button.Icon(Ui.IconName.Bars3BottomRight)
        ]]));

        yield return ("attached-button", Row(Div[Ui.ButtonGroup[
            Ui.Button["New product"],
            Ui.Button.Icon(Ui.IconName.ChevronDown)
        ]]));

        yield return ("as-a-link", Div[
            Ui.Button.Href("https://google.com").IconTrailing(Ui.IconName.ArrowUpRight)["Visit Google"]
        ]);

        yield return ("square", Div[Ui.Button.Square()["..."]]);

        yield return ("inset", Div.Style("display:flex;justify-content:space-between;width:288px")[
            Ui.Heading["Post successfully created."],
            Ui.Button.Ghost.Sm.Icon(Ui.IconName.XMark).Inset(Ui.Inset.All)
        ]);
    }

    private static Component Swatches(Ui.Color hue) => Div.Style("display:flex;gap:12px")[
        Ui.Button.Primary.Color(hue)["Primary"],
        Ui.Button.Filled.Color(hue)["Filled"],
        Ui.Button.Color(hue)["Outline"],
        Ui.Button.Ghost.Color(hue)["Ghost"],
        Ui.Button.Subtle.Color(hue)["Subtle"]
    ];
}
