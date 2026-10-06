using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/icon</c>, example for example.
/// </summary>
/// <remarks>
///     Two of that page's sections are not here because they are not the component: "Lucide icons" is an
///     artisan command that copies SVG files into a Laravel project, and "Custom icons" is a Blade file the
///     app writes itself. Neither example carries <c>data-flux-icon</c>, so there is nothing to pair.
/// </remarks>
public sealed partial class IconParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class: the kit's sheet
    // holds only what the kit writes.
    private const string AppUtilities =
        "<style>.size-12{width:3rem;height:3rem}.size-10{width:2.5rem;height:2.5rem}.size-8{width:2rem;height:2rem}"
        + ".text-amber-500{color:oklch(76.9% .188 70.08)}"
        + ".dark\\:text-amber-300:where(.dark,.dark *){color:oklch(87.9% .169 91.605)}</style>";

    public override string Page => "icon";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Alone in a block on Flux's page, where every later example is a flex row.
        yield return ("", Div[Raw.Value(AppUtilities), Ui.Icon.Name(Ui.IconName.Bolt)]);

        yield return ("variants", Row(
            Ui.Icon.Name(Ui.IconName.Bolt),
            Ui.Icon.Name(Ui.IconName.Bolt).Solid,
            Ui.Icon.Name(Ui.IconName.Bolt).Mini,
            Ui.Icon.Name(Ui.IconName.Bolt).Micro));

        yield return ("sizes", Row(
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-12"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-10"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-8")));

        yield return ("color", Row(Ui.Icon.Name(Ui.IconName.Bolt).Solid.Class("text-amber-500 dark:text-amber-300")));

        yield return ("loading-spinner", Row(Ui.Icon.Name(Ui.IconName.Loading)));

        // `<flux:icon name="bolt" />` — Blade's way of taking the name from a variable. Name always does.
        yield return ("dynamic-icons", Row(Ui.Icon.Name(Ui.IconName.Bolt)));
    }
}
