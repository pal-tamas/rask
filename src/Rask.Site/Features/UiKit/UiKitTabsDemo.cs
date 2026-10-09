namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux's tabs, example by example in the order fluxui.dev/components/tabs gives them, then a row with a
///     disabled tab and counts.
/// </summary>
/// <remarks>
///     The first row hands its selected tab to a handler and the segmented one is bound to a property; every
///     other row keeps track of its own selected tab, with nothing here holding it.
/// </remarks>
public sealed partial class UiKitTabsDemo : Component
{
    private static readonly string[] Sections =
        ["Profile", "Account", "Billing", "Security", "Notifications", "Integrations", "API"];

    private readonly List<string> _tabs = ["Tab #1", "Tab #2"];

    private string _settings = "profile";

    private string View { get; set; } = "list";

    /// <inheritdoc />
    protected override Component? Render() => [OverPanels(), Rows(), Variants(), Dynamic(), DisabledAndCounts()];

    private Component OverPanels() =>
    [
        Example("Tabs", "ui-tabs",
            Ui.TabGroup[
                Ui.Tabs.Value(_settings).OnChange(tab => _settings = tab)[
                    Ui.Tab.Name("profile")["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Ui.TabPanel.Name("profile").Class("pt-4! text-sm")["Your name, your photo and what others see."],
                Ui.TabPanel.Name("account").Class("pt-4! text-sm")["Your email address and how you sign in."],
                Ui.TabPanel.Name("billing").Class("pt-4! text-sm")["Your plan and your invoices."]
            ],
            P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-tabs-state"))[$"Selected: {_settings}."]),

        Example("Findable tabs", "ui-tabs-findable",
            Ui.TabGroup.Findable()[
                Ui.Tabs[
                    Ui.Tab.Name("profile").Selected()["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Ui.TabPanel.Name("profile").Class("px-2 pt-6! text-sm")["Manage your public profile and personal information."],
                Ui.TabPanel.Name("account").Class("px-2 pt-6! text-sm")["Update your email address, password, and security settings."],
                Ui.TabPanel.Name("billing").Class("px-2 pt-6! text-sm")["Invoice INV-1042 is due on September 1."]
            ])
    ];

    private static Component Rows() =>
    [
        Example("With icons", "ui-tabs-icons",
            Ui.Tabs[
                Ui.Tab.Name("profile").Icon(Ui.IconName.User)["Profile"],
                Ui.Tab.Name("account").Icon(Ui.IconName.Cog6Tooth)["Account"],
                Ui.Tab.Name("billing").Icon(Ui.IconName.Banknotes)["Billing"]
            ]),

        Example("Padded edges", "ui-tabs-padded",
            Ui.Tabs.Class("px-4")[
                Ui.Tab.Name("profile")["Profile"],
                Ui.Tab.Name("account")["Account"],
                Ui.Tab.Name("billing")["Billing"]
            ]),

        Example("Scrollable tabs", "ui-tabs-scrollable",
            Div.Class("max-w-sm")[Ui.Tabs.Scrollable()[Sections.Select(Section)]]),

        Example("Scrollable tabs, faded edge", "ui-tabs-fade",
            Div.Class("max-w-sm")[Ui.Tabs.Scrollable().ScrollableFade()[Sections.Select(Section)]])
    ];

    private Component Variants() =>
    [
        Example("Segmented tabs", "ui-tabs-segmented",
            Ui.Tabs.Segmented.Bind(() => View)[
                Ui.Tab.Name("list")["List"],
                Ui.Tab.Name("board")["Board"],
                Ui.Tab.Name("timeline")["Timeline"]
            ]),

        Example("Segmented with icons", "ui-tabs-segmented-icons",
            Ui.Tabs.Segmented[
                Ui.Tab.Icon(Ui.IconName.ListBullet)["List"],
                Ui.Tab.Icon(Ui.IconName.Squares2x2)["Board"],
                Ui.Tab.Icon(Ui.IconName.CalendarDays)["Timeline"]
            ]),

        Example("Small segmented tabs", "ui-tabs-segmented-sm",
            Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)[Ui.Tab["Demo"], Ui.Tab["Code"]]),

        Example("Pill tabs", "ui-tabs-pills",
            Ui.Tabs.Pills[Ui.Tab["List"], Ui.Tab["Board"], Ui.Tab["Timeline"]])
    ];

    private Component Dynamic() =>
    [
        Example("Dynamic tabs", "ui-tabs-dynamic",
            Ui.TabGroup[
                Ui.Tabs[
                    _tabs.Select(tab => Ui.Tab.Key(tab).Name(tab)[tab]),
                    Ui.Tab.Key("add").Icon(Ui.IconName.Plus).Action().OnClick(AddTab)["Add tab"]
                ],
                _tabs.Select(tab => Ui.TabPanel.Key("panel " + tab).Name(tab).Class("pt-4! text-sm")[$"What {tab} holds."])
            ])
    ];

    private static Component DisabledAndCounts() =>
        Example("A disabled tab, and a count", "ui-tabs-disabled",
            Ui.Tabs[
                Ui.Tab.Name("open")["Open", Ui.Badge.Sm["12"]],
                Ui.Tab.Name("archived").Disabled()["Archived"],
                Ui.Tab.Name("failed")["Failed", Ui.Badge.Sm.Solid.Color(Ui.Color.Red)["3"]]
            ]);

    private void AddTab() => _tabs.Add($"Tab #{_tabs.Count + 1}");

    private static Component Section(string name) => Ui.Tab.Key(name).Name(name.ToLowerInvariant())[name];

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Example(string heading, string testid, params Component[] body) =>
        Div.Key(testid).Class("mb-6").Data(Testid(testid))[
            H3.Class("mb-2 text-sm font-medium text-ui-muted")[heading],
            body
        ];
}
