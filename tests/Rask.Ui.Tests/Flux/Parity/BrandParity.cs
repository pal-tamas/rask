using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/brand</c>, example for example.
/// </summary>
/// <remarks>
///     Flux's page draws its image examples twice — one logo for light, one for dark, each hidden in the other
///     scheme — and so do these. The last example sits in Flux's header, which the layouts own: a plain
///     <c>&lt;header&gt;</c> of the same box stands in for it, and what is inside it is compared in full.
/// </remarks>
public sealed partial class BrandParity : FluxParity
{
    private const string Logo = "https://fluxui.dev/img/demo/logo.png";

    private const string DarkLogo = "https://fluxui.dev/img/demo/dark-mode-logo.png";

    public override string Page => "brand";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Div[
            Ui.Brand.Href("#").Logo(Logo).Name("Acme Inc.").Class("only-light"),
            Ui.Brand.Href("#").Logo(DarkLogo).Name("Acme Inc.").Class("only-dark")
        ]));

        // Flux's markdown says size-6 for this mark; its page draws 28px, and the page is what is measured.
        yield return ("logo-slot", Row(Div[
            Ui.Brand.Href("#").Name("Launchpad").Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro).LogoClass("logo-launchpad")
        ]));

        yield return ("logo-only", Row(Div[
            Ui.Brand.Href("#").Logo(Logo).Class("only-light"),
            Ui.Brand.Href("#").Logo(DarkLogo).Class("only-dark")
        ]));

        yield return ("examples", Row(Div[
            Header.Class("stand-in-bar").Style("z-index:10").Attributes(("data-ui-header", null))[
                Ui.Brand.Href("#").Name("Acme Inc.").Logo(I.Style(NavigationStandIns.Bold)["A"]).LogoClass("logo-accent"),
                Ui.Navbar[
                    Ui.NavbarItem.Href("#").Current(true)["Home"],
                    Ui.NavbarItem.Href("#").Badge("12")["Inbox"]
                ],
                Div.Style("flex:1 1 0%;min-width:96px").Attributes(("data-ui-spacer", null)),
                Ui.Profile.Circle().Chevron(false).Avatar(NavigationStandIns.Caleb)
            ]
        ]));
    }
}
