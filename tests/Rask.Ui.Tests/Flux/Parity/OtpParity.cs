using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/otp-input</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The values are the rendered page's: the licence key there holds <c>L49R4</c> and the PIN <c>1234</c>.
///     </para>
///     <para>
///     Flux's button is another page's component and not rebuilt yet, so the two in the first form are stand-ins
///     marked <c>data-parity-skip</c>: the room each takes on Flux's page, held to its place and size.
///     </para>
/// </remarks>
public sealed partial class OtpParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class: the kit's sheet
    // holds only what the kit writes. Layered, as an app's are: the field's own margin under a label is a
    // utility too, and it is the later one.
    private const string AppUtilities =
        "<style>@layer base{.mx-auto{margin-inline:auto}.text-center{text-align:center}"
        + ".sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap;border-width:0}"
        + "}</style>";

    private const string Intro = "max-width:256px;margin:0 auto 32px";

    public override string Page => "otp-input";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Centered(Ui.Otp.Value("").Length(6).Class("mx-auto")));

        // label:sr-only, error:icon and error:class there are the shorthand's pass-throughs to the label and the
        // error; here the field is written out and each takes its own.
        yield return ("example-usage", Div.Style("width:384px;margin:0 auto")[
            Raw.Value(AppUtilities),
            Ui.Card[
                Form.Model(new object())[
                    Div.Style(Intro)[Heading(), Words()],
                    Div.Style("margin-bottom:32px")[
                        Ui.Field[
                            Ui.Label.Class("sr-only")["OTP Code"],
                            Ui.Otp.Value("").Length(6).Class("mx-auto"),
                            Ui.Error.Icon(false).Class("text-center")
                        ]
                    ],
                    Div[
                        StandIn("height:40px;margin-bottom:16px", "Verify"),
                        StandIn("height:40px", "Resend code")
                    ]
                ]
            ]
        ]);

        // submit="auto" there submits the form; here the full code arrives at OnComplete.
        yield return ("autosubmit", Div.Style("width:384px;margin:0 auto")[
            Raw.Value(AppUtilities),
            Form.Model(new object())[
                Div.Style(Intro)[Heading(), Words()],
                Div[Ui.Otp.Value("").Length(6).OnComplete(_ => { }).Class("mx-auto")]
            ]
        ]);

        yield return ("alphanumeric", Centered(
            Ui.Otp.Value("L49R4").Length(10).Alphanumeric.Autocomplete("off").Label("License key")
                .DescriptionTrailing("Enter the license key printed on the installation disc")));

        yield return ("private", Centered(Ui.Otp.Value("1234").Length(4).Private().Label("PIN Code")));

        yield return ("separator", Centered(
            Ui.Otp.Value("")[
                Ui.OtpInput, Ui.OtpInput, Ui.OtpInput,
                Ui.OtpSeparator,
                Ui.OtpInput, Ui.OtpInput, Ui.OtpInput
            ]));

        yield return ("group", Centered(
            Ui.Otp.Value("")[
                Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput]
            ]));

        yield return ("group-separator", Centered(
            Ui.Otp.Value("")[
                Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput],
                Ui.OtpSeparator,
                Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput]
            ]));
    }

    private static Component Heading() =>
        Ui.Heading.Lg.Style("text-align:center;margin-bottom:8px")["Verify your account"];

    private static Component Words() =>
        Ui.Text.Style("text-align:center")["Please enter a one-time password from the authenticator app."];

    // The docs page centres each example in a flex row, so the row of cells is as wide as its cells.
    private static Component Centered(Component example) =>
        Div.Style("display:flex;justify-content:center")[Raw.Value(AppUtilities), Div[example]];

    private static Component StandIn(string box, string text) =>
        Div.Data("parity-skip").Style(box + ";overflow:hidden;font-size:14px;line-height:20px;white-space:nowrap")[text];
}
