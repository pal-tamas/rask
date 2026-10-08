using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/checkbox, example by example.</summary>
/// <remarks>
///     The words and the ticked boxes are the LIVE page's, which differ here and there from the code printed
///     under each example. Components from other pages that are not rebuilt yet — the table of the check-all
///     example, the heading and text of the custom cards — are stand-ins marked <c>data-parity-skip</c>, held
///     to their place and size.
/// </remarks>
public sealed partial class CheckboxParity : FluxParity
{
    public override string Page => "checkbox";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Shrunk(
            Ui.Field.Variant(Ui.FieldVariant.Inline)[
                Ui.Checkbox.Id("terms"),
                Ui.Label["I agree to the terms and conditions"],
                Ui.Error.Name("terms")
            ]));

        yield return ("checkbox-group", Shrunk(
            Ui.CheckboxGroup.Values(["push", "email"]).Id("notifications").Label("Notifications")[
                Ui.Checkbox.Value("push").Label("Push notifications"),
                Ui.Checkbox.Value("email").Label("Email"),
                Ui.Checkbox.Value("app").Label("In-app alerts"),
                Ui.Checkbox.Value("sms").Label("SMS")
            ]));

        yield return ("with-descriptions", Wide(384,
            Ui.CheckboxGroup.Values(["newsletter"]).Id("described").Label("Subscription preferences")[
                Ui.Checkbox.Value("newsletter").Label("Newsletter")
                    .Description("Receive our monthly newsletter with the latest updates and offers."),
                Ui.Checkbox.Value("updates").Label("Product updates")
                    .Description("Stay informed about new features and product updates."),
                Ui.Checkbox.Value("invitations").Label("Event invitations")
                    .Description("Get invitations to our exclusive events and webinars.")
            ]));

        // <div class="flex gap-4 *:gap-x-2">: the docs page narrows each field's gap from outside, as the
        // sheet beside this example does.
        yield return ("horizontal-fieldset", Shrunk(
            Raw.Value("<style>#languages>[data-ui-field]{column-gap:8px}</style>"),
            Ui.Fieldset[
                Ui.Legend["Languages"],
                Ui.Description["Choose the languages you want to support."],
                Div.Id("languages").Style("display:flex;gap:16px")[
                    Ui.Checkbox.Id("english").Checked().Label("English"),
                    Ui.Checkbox.Id("spanish").Checked().Label("Spanish"),
                    Ui.Checkbox.Id("french").Label("French"),
                    Ui.Checkbox.Id("german").Label("German")
                ]
            ]));

        yield return ("check-all", Wide(384, CheckAll()));

        yield return ("checked", Shrunk(Ui.Checkbox.Id("enabled").Label("Enable notifications").Checked()));

        yield return ("disabled", Shrunk(Ui.Checkbox.Id("read-write").Label("Read and write").Disabled()));

        yield return ("checkbox-cards", Wide(542,
            Cards("cards").Class("max-sm:flex-col")[
                Card("newsletter", "Newsletter", "Get the latest updates and offers."),
                Card("updates", "Product updates", "Learn about new features and products."),
                Card("invitations", "Event invitations", "Invitatations to exclusive events.")
            ]));

        yield return ("vertical-cards", Wide(320,
            Cards("vertical").Class("flex-col")[
                Card("newsletter", "Newsletter", "Get the latest updates and offers."),
                Card("updates", "Product updates", "Learn about new features and products."),
                Card("invitations", "Event invitations", "Invitatations to exclusive events.")
            ]));

        yield return ("cards-with-icons", Wide(320,
            Cards("icons").Class("flex-col")[
                Card("newsletter", "Newsletter", "Get the latest updates and offers.").Icon(Ui.IconName.Newspaper),
                Card("updates", "Product updates", "Learn about new features and products.").Icon(Ui.IconName.Cube),
                Card("invitations", "Event invitations", "Invitatations to exclusive events.").Icon(Ui.IconName.Calendar)
            ]));

        yield return ("custom-card-content", Wide(320,
            Cards("custom").Class("flex-col")[
                Custom("newsletter", "Newsletter", "Get the latest updates and offers."),
                Custom("updates", "Product updates", "Learn about new features and products."),
                Custom("invitations", "Event invitations", "Invitatations to exclusive events.")
            ]));

        yield return ("pills", Wide(288,
            Ui.CheckboxGroup.Values(["fantasy", "science-fiction", "thriller"]).Id("categories").Label("Categories")
                .Variant(Ui.CheckboxGroupVariant.Pills)[
                Ui.Checkbox.Value("fantasy").Label("Fantasy"),
                Ui.Checkbox.Value("science-fiction").Label("Science fiction"),
                Ui.Checkbox.Value("horror").Label("Horror"),
                Ui.Checkbox.Value("mystery").Label("Mystery"),
                Ui.Checkbox.Value("romance").Label("Romance"),
                Ui.Checkbox.Value("autobiography").Label("Autobiography"),
                Ui.Checkbox.Value("thriller").Label("Thriller"),
                Ui.Checkbox.Value("poetry").Label("Poetry"),
                Ui.Checkbox.Value("children").Label("Children")
            ]));

        yield return ("buttons", Shrunk(
            Ui.CheckboxGroup.Values(["notifications", "analytics"]).Id("features").Label("Features")
                .Variant(Ui.CheckboxGroupVariant.Buttons)[
                Ui.Checkbox.Value("notifications").Icon(Ui.IconName.Bell).Label("Notifications"),
                Ui.Checkbox.Value("analytics").Icon(Ui.IconName.ChartBar).Label("Analytics"),
                Ui.Checkbox.Value("backups").Icon(Ui.IconName.CloudArrowUp).Label("Backups")
            ]));
    }

    private static UiCheckboxGroup<string> Cards(string id) =>
        Ui.CheckboxGroup.Values(["newsletter"]).Id(id).Label("Subscription preferences").Variant(Ui.CheckboxGroupVariant.Cards);

    private static UiCheckbox Card(string value, string label, string description) =>
        Ui.Checkbox.Value(value).Label(label).Description(description);

    // <flux:heading class="leading-4"> and <flux:text size="sm" class="mt-2">: other pages' components.
    private static Component Custom(string value, string heading, string text) =>
        Ui.Checkbox.Value(value)[
            Ui.CheckboxIndicator,
            Div.Style("flex:1 1 0%")[
                Div.Data("parity-skip", "").Style("font-size:14px;line-height:16px;font-weight:500")[heading],
                P.Data("parity-skip", "").Style("margin-top:8px;font-size:12px;line-height:16px")[text]
            ]
        ];

    // The example sits in Flux's table, which is another page's: every part of it is a stand-in that keeps
    // its own look to itself (`self`) and lets the checkboxes inside it be compared. The sheet is the
    // table's, and the 4px its cells put before a checkbox.
    private const string TableSheet =
        "<style>#people{max-width:256px;margin:auto}#people [data-ui-checkbox]{margin-left:4px}"
        + "#people table{min-width:100%;border-collapse:separate;border-spacing:0;white-space:nowrap;"
        + "font-size:14px;line-height:20px;color:oklch(0.274 0.006 286.033)}"
        + "#people th,#people td{padding:12px 0}#people :is(th,td):first-child{padding-right:12px}"
        + "#people :is(th,td):last-child{padding-left:12px}#people th{border-bottom:1px solid #0001;font-weight:500;text-align:start}"
        + "#people td{color:oklch(0.552 0.016 285.938)}#people tr+tr>td{border-top:1px solid #0001}"
        + ".dark #people table{color:#fff}.dark #people td{color:oklch(0.871 0.006 286.286)}</style>";

    private static Component CheckAll() =>
        Div[
            Raw.Value(TableSheet),
            Ui.CheckboxGroup.Values(["caleb"]).Id("people")[
                Div.Style("display:flex;flex-direction:column").Data(Skip, Self)[
                    Div.Style("overflow-x:auto").Data(Skip, Self)[
                        Table.Data(Skip, Self)[
                            Thead.Data(Skip, Self)[
                                Tr.Data(Skip, Self)[
                                    Th.Data(Skip, Self)[Div.Style("display:flex").Data(Skip, Self)[Ui.CheckboxAll]],
                                    Th.Data(Skip, Self)[Div.Style("display:flex").Data(Skip, Self)["Name"]]
                                ]
                            ],
                            Tbody.Data(Skip, Self)[
                                Person("caleb", "Caleb Porzio"),
                                Person("hugo", "Hugo Sainte-Marie"),
                                Person("keith", "Keith Damiani")
                            ]
                        ]
                    ]
                ]
            ]
        ];

    private static Component Person(string value, string name) =>
        Tr.Data(Skip, Self)[Td.Data(Skip, Self)[Ui.Checkbox.Value(value)], Td.Data(Skip, Self)[name]];

    private const string Skip = "parity-skip";

    private const string Self = "self";

    // Centred at its own width, as the docs page lays out an example that does not fill the column.
    private static Component Shrunk(params Component[] example) =>
        Div.Style("display:flex;justify-content:center")[Div[example]];

    private static Component Wide(int width, Component example) =>
        Div.Style(FormattableString.Invariant($"width:{width}px;margin:0 auto"))[example];
}
