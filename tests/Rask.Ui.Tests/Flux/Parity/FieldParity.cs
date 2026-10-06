using System.Linq.Expressions;
using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/field, example by example.</summary>
/// <remarks>
///     The inputs and the select in these examples are not rebuilt yet, so each is a <see cref="Control" />
///     stand-in: a box of the size Flux's control measures, marked <c>data-parity-skip</c> so the tool holds
///     it to its place and its size and leaves its inside to the input's own page. Everything else — the
///     field, the label and its badge, the description, the error, the fieldset and its legend — is compared
///     whole.
/// </remarks>
public sealed partial class FieldParity : FluxParity
{
    // The column Flux's docs page sets an example in.
    private const string Column = "max-width:384px;margin:0 auto";

    public override string Page => "field";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style("max-width:384px;margin:0 auto")[
            Ui.Field[Ui.Label["Email"], Control("email"), Ui.Error.Name("email")]
        ]);

        // "Shorthand props" has no rendered example on Flux's page; split-layout and fieldset below use it.
        yield return ("with-trailing-description", Div.Style(Column)[
            Ui.Field[
                Ui.Label["Password"],
                Control("password"),
                Ui.Error.Name("password"),
                Ui.Description["Must be at least 8 characters long, include an uppercase letter, a number, and a special character."]
            ]
        ]);

        yield return ("with-badge", Div.Style(Column)[
            SpaceY(6,
                Ui.Field[Ui.Label.Badge("Required")["Email"], Control("email-required"), Ui.Error.Name("email")],
                Ui.Field[Ui.Label.Badge("Optional")["Phone number"], Control("phone"), Ui.Error.Name("phone")])
        ]);

        yield return ("split-layout", Div.Style("width:480px;margin:0 auto;" + Grid("16px"))[
            Shorthand("First name"),
            Shorthand("Last name")
        ]);

        yield return ("fieldset", Div.Style("width:542px;margin:0 auto")[
            Ui.Fieldset[
                Ui.Legend["Shipping address"],
                SpaceY(6,
                    Shorthand("Street address line 1", "max-width:384px"),
                    Shorthand("Street address line 2", "max-width:384px"),
                    Div.Style(Grid("24px 16px"))[
                        Shorthand("City"),
                        Shorthand("State / Province"),
                        Shorthand("Postal / Zip code"),
                        Shorthand("Country")
                    ])
            ]
        ]);
    }

    private static string Grid(string gap) => "display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:" + gap;

    // Stand-in for Flux's input (and its native select), neither rebuilt yet: the room one takes, nothing more.
    private static Component Control(string id, string? style = null) =>
        Div.Id(id).Data("parity-skip", "").Style("height:40px;border:1px solid #d4d4d8;border-radius:8px;" + style);

    // What a kit control does with its Label prop: the same field, drawn by the control around itself.
    private static Component Shorthand(string label, string? style = null)
    {
        var field = UiWithField.For(new StandIn(UiFieldId.Derive(null, null, label)), label);

        return field.Wrap(Control(field.ControlId, style));
    }

    private sealed record StandIn(string ControlId) : IUiFieldControl
    {
        public LambdaExpression? Bound => null;
    }
}
