using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/radio, example by example.</summary>
/// <remarks>
///     The words and the chosen radios are the LIVE page's, which differ here and there from the code printed
///     under each example. The heading and text of the custom cards are another page's components, not
///     rebuilt yet: stand-ins marked <c>data-parity-skip</c>, held to their place and size.
/// </remarks>
public sealed partial class RadioParity : FluxParity
{
    public override string Page => "radio";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Shrunk(
            Ui.RadioGroup.Value("cc").Id("payment").Label("Select your payment method")[
                Ui.Radio.Value("cc").Label("Credit Card"),
                Ui.Radio.Value("paypal").Label("Paypal"),
                Ui.Radio.Value("ach").Label("Bank transfer")
            ]));

        yield return ("with-descriptions", Wide(384,
            Ui.RadioGroup.Value("administrator").Id("described").Label("Role")[Roles()]));

        yield return ("within-fieldset", Wide(384,
            Ui.Fieldset[
                Ui.Legend["Role"],
                Ui.RadioGroup.Value("administrator").Id("fieldset")[Roles()]
            ]));

        yield return ("segmented", Wide(288,
            Ui.RadioGroup.Value("administrator").Id("segmented").Label("Role").Segmented[Segments()]));

        yield return ("segmented", Wide(288,
            Ui.RadioGroup.Value("administrator").Id("segmented-sm").Label("Role").Segmented.Sm[Segments()]));

        yield return ("segmented-with-icons", Wide(384,
            Ui.RadioGroup.Value("administrator").Id("segmented-icons").Label("Role").Segmented[
                Ui.Radio.Value("administrator").Label("Admin").Icon(Ui.IconName.Wrench),
                Ui.Radio.Value("editor").Label("Editor").Icon(Ui.IconName.PencilSquare),
                Ui.Radio.Value("viewer").Label("Viewer").Icon(Ui.IconName.Eye)
            ]));

        yield return ("radio-cards", Wide(542,
            Shipping("cards").Class("max-sm:flex-col")[Cards(icons: false)]));

        yield return ("vertical-cards", Wide(320,
            Shipping("vertical").Class("flex-col")[Cards(icons: false)]));

        yield return ("cards-with-icons", Wide(542,
            Shipping("icons").Class("max-sm:flex-col")[Cards(icons: true)]));

        yield return ("cards-without-indicators", Wide(542,
            Shipping("bare").Indicator(false).Class("max-sm:flex-col")[Cards(icons: true)]));

        yield return ("custom-card-content", Wide(542,
            Shipping("custom").Class("max-sm:flex-col")[
                Custom("standard", "Standard", "4-10 business days"),
                Custom("fast", "Fast", "2-5 business days"),
                Custom("next-day", "Next day", "1 business day")
            ]));

        yield return ("pills", Wide(288,
            Ui.RadioGroup.Value("medium").Id("priority").Label("Priority").Pills[
                Ui.Radio.Value("low").Label("Low"),
                Ui.Radio.Value("medium").Label("Medium"),
                Ui.Radio.Value("high").Label("High"),
                Ui.Radio.Value("critical").Label("Critical")
            ]));

        // class="w-full *:flex-1": the docs page stretches the buttons from outside.
        yield return ("buttons", Wide(446,
            Ui.RadioGroup.Value("bug").Id("feedback").Label("Feedback type").Buttons[
                Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt).Class("flex-1")["Bug report"],
                Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb).Class("flex-1")["Suggestion"],
                Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle).Class("flex-1")["Question"]
            ]));
    }

    private static Component[] Roles() =>
    [
        Ui.Radio.Value("administrator").Label("Administrator").Description("Administrator users can perform any action."),
        Ui.Radio.Value("editor").Label("Editor").Description("Editor users have the ability to read, create, and update."),
        Ui.Radio.Value("viewer").Label("Viewer")
            .Description("Viewer users only have the ability to read. Create, and update are restricted."),
    ];

    private static Component[] Segments() =>
    [
        Ui.Radio.Value("administrator").Label("Admin"),
        Ui.Radio.Value("editor").Label("Editor"),
        Ui.Radio.Value("viewer").Label("Viewer"),
    ];

    private static UiRadioGroup<string> Shipping(string id) =>
        Ui.RadioGroup.Value("standard").Id(id).Label("Shipping").Cards;

    private static Component[] Cards(bool icons) =>
    [
        Ui.Radio.Value("standard").Icon(icons ? Ui.IconName.Truck : null).Label("Standard").Description("4-10 business days"),
        Ui.Radio.Value("fast").Icon(icons ? Ui.IconName.Cube : null).Label("Fast").Description("2-5 business days"),
        Ui.Radio.Value("next-day").Icon(icons ? Ui.IconName.Clock : null).Label("Next day").Description("1 business day"),
    ];

    // <flux:heading class="leading-4"> and <flux:text size="sm" class="mt-2">: other pages' components.
    private static Component Custom(string value, string heading, string text) =>
        Ui.Radio.Value(value)[
            Ui.RadioIndicator,
            Div.Style("flex:1 1 0%")[
                Div.Data("parity-skip", "").Style("font-size:14px;line-height:16px;font-weight:500")[heading],
                P.Data("parity-skip", "").Style("margin-top:8px;font-size:12px;line-height:16px")[text]
            ]
        ];

    // Centred at its own width, as the docs page lays out an example that does not fill the column.
    private static Component Shrunk(Component example) =>
        Div.Style("display:flex;justify-content:center")[Div[example]];

    private static Component Wide(int width, Component example) =>
        Div.Style(FormattableString.Invariant($"width:{width}px;margin:0 auto"))[example];
}
