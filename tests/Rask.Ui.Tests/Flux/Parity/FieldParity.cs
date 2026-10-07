using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/field, example by example.</summary>
/// <remarks>
///     The inputs and the select are the real ones, so every example is compared whole. Flux's page writes
///     <c>required</c> on one input; it changes nothing that is drawn, and the kit's input takes no such prop.
/// </remarks>
public sealed partial class FieldParity : FluxParity
{
    // The column Flux's docs page sets an example in.
    private const string Column = "max-width:384px;margin:0 auto";

    // What an app's own Tailwind build emits for the class these examples hand to Class.
    private const string AppUtilities = "<style>.max-w-sm{max-width:24rem}</style>";

    public override string Page => "field";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style("max-width:384px;margin:0 auto")[
            Raw.Value(AppUtilities),
            Ui.Field[Ui.Label["Email"], Ui.Input.Of<string>().Id("email").Type(InputType.Email), Ui.Error.Name("email")]
        ]);

        // "Shorthand props" has no rendered example on Flux's page; split-layout and fieldset below use it.
        yield return ("with-trailing-description", Div.Style(Column)[
            Ui.Field[
                Ui.Label["Password"],
                Ui.Input.Of<string>().Id("password").Type(InputType.Password),
                Ui.Error.Name("password"),
                Ui.Description["Must be at least 8 characters long, include an uppercase letter, a number, and a special character."]
            ]
        ]);

        yield return ("with-badge", Div.Style(Column)[
            SpaceY(6,
                Ui.Field[Ui.Label.Badge("Required")["Email"], Ui.Input.Of<string>().Id("email-required").Type(InputType.Email), Ui.Error.Name("email")],
                Ui.Field[
                    Ui.Label.Badge("Optional")["Phone number"],
                    Ui.Input.Of<string>().Id("phone").Placeholder("(555) 555-5555").Mask("(999) 999-9999"),
                    Ui.Error.Name("phone")
                ])
        ]);

        yield return ("split-layout", Div.Style("width:480px;margin:0 auto;" + Grid("16px"))[
            Ui.Input.Of<string>().Label("First name").Placeholder("River"),
            Ui.Input.Of<string>().Label("Last name").Placeholder("Porzio")
        ]);

        yield return ("fieldset", Div.Style("width:542px;margin:0 auto")[
            Ui.Fieldset[
                Ui.Legend["Shipping address"],
                SpaceY(6,
                    Ui.Input.Of<string>().Label("Street address line 1").Placeholder("123 Main St").Class("max-w-sm"),
                    Ui.Input.Of<string>().Label("Street address line 2").Placeholder("Apartment, studio, or floor").Class("max-w-sm"),
                    Div.Style(Grid("24px 16px"))[
                        Ui.Input.Of<string>().Label("City").Placeholder("San Francisco"),
                        Ui.Input.Of<string>().Label("State / Province").Placeholder("CA"),
                        Ui.Input.Of<string>().Label("Postal / Zip code").Placeholder("12345"),
                        Ui.Select.Value("United States").Label("Country")[Ui.SelectOption["United States"]]
                    ])
            ]
        ]);
    }

    private static string Grid(string gap) => "display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:" + gap;
}
