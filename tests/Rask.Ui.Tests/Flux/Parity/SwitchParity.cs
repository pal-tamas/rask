using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/switch, example by example.</summary>
/// <remarks>
///     The separators between the switches of the fieldset are another page's component, not rebuilt yet:
///     stand-ins marked <c>data-parity-skip</c>, held to their place and size.
/// </remarks>
public sealed partial class SwitchParity : FluxParity
{
    public override string Page => "switch";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style("display:flex;justify-content:center")[
            Div[
                Ui.Field.Variant(Ui.FieldVariant.Inline)[
                    Ui.Label["Enable notifications"],
                    Ui.Switch.Value(false).Id("notifications"),
                    Ui.Error.Name("notifications")
                ]
            ]
        ]);

        yield return ("fieldset", Div.Style("width:384px;margin:0 auto")[
            Ui.Fieldset[
                Ui.Legend["Email notifications"],
                SpaceY(4,
                    Emails("Communication emails", "Receive emails about your account activity."),
                    Separator(),
                    Emails("Marketing emails", "Receive emails about new products, features, and more."),
                    Separator(),
                    Emails("Social emails", "Receive emails for friend requests, follows, and more."),
                    Separator(),
                    Emails("Security emails", "Receive emails about your account activity and security."))
            ]
        ]);

        yield return ("left-align", Div.Style("width:384px;margin:0 auto")[
            Ui.Fieldset[
                Ui.Legend["Email notifications"],
                SpaceY(3,
                    Ui.Switch.Value(false).Id("left-communication").Label("Communication emails").Left,
                    Ui.Switch.Value(false).Id("left-marketing").Label("Marketing emails").Left,
                    Ui.Switch.Value(false).Id("left-social").Label("Social emails").Left,
                    Ui.Switch.Value(false).Id("left-security").Label("Security emails").Left)
            ]
        ]);
    }

    private static Component Emails(string label, string description) =>
        Ui.Switch.Value(false).Label(label).Description(description);

    // <flux:separator variant="subtle" />
    private static Component Separator() => Div.Data("parity-skip", "").Style("height:1px");
}
