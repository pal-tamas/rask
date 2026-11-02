using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/checkbox, example by example.</summary>
/// <remarks>
///     The words and the ticked boxes are the LIVE page's, which differ here and there from the code printed
///     under each example. What other pages draw — the table of the check-all example, the heading and text of
///     the custom cards — is the real component too.
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

        yield return ("custom-card-content", Wide(320, Div[
            Raw.Value(CustomSheet),
            Cards("custom").Class("flex-col")[
                Custom("newsletter", "Newsletter", "Get the latest updates and offers."),
                Custom("updates", "Product updates", "Learn about new features and products."),
                Custom("invitations", "Event invitations", "Invitatations to exclusive events.")
            ]]));

        yield return ("pills", DocsInset(
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

    // `leading-4` and `mt-2` there: an app's own utilities, stated under names of this page's own.
    private const string CustomSheet = "<style>.parity-leading-4{line-height:1rem}.parity-mt-2{margin-top:.5rem}</style>";

    // <flux:heading class="leading-4"> over <flux:text size="sm" class="mt-2">.
    private static Component Custom(string value, string heading, string text) =>
        Ui.Checkbox.Value(value)[
            Ui.CheckboxIndicator,
            Div.Style("flex:1 1 0%")[
                Ui.Heading.Class("parity-leading-4")[heading],
                Ui.Text.Sm.Class("parity-mt-2")[text]
            ]
        ];

    // `max-w-64 m-auto` on the group and `ml-1` on each checkbox there: an app's own utilities.
    private const string TableSheet = "<style>.parity-people{max-width:16rem;margin:auto}.parity-ml-1{margin-left:.25rem}</style>";

    // The group around Flux's table: the check-all box heads the first column, a box starts each row.
    private static Component CheckAll() =>
        Div[
            Raw.Value(TableSheet),
            Ui.CheckboxGroup.Values(["caleb"]).Id("people").Class("parity-people")[
                Ui.Table[
                    Ui.TableColumns[
                        Ui.TableColumn[Ui.CheckboxAll.Class("parity-ml-1")],
                        Ui.TableColumn["Name"]
                    ],
                    Ui.TableRows[
                        Person("caleb", "Caleb Porzio"),
                        Person("hugo", "Hugo Sainte-Marie"),
                        Person("keith", "Keith Damiani")
                    ]
                ]
            ]
        ];

    private static Component Person(string value, string name) =>
        Ui.TableRow.Key(value)[Ui.TableCell[Ui.Checkbox.Value(value).Class("parity-ml-1")], Ui.TableCell[name]];

    // Centred at its own width in the docs page's 384px column, as it lays out an example that does not fill it: one
    // a little wider than the column wraps there (the three buttons, whose words are whole pixels on Linux).
    private static Component Shrunk(params Component[] example) => DocsColumn(Div[example]);

    private static Component Wide(int width, Component example) =>
        Div.Style(FormattableString.Invariant($"max-width:{width}px;margin:0 auto"))[example];
}
