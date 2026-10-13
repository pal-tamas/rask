using Rask.Core;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A page of kit components for <see cref="KitDrawsWithoutAskingTests" />: each under <c>[data-c=name]</c>,
///     with the kit's own sheet, and given NO callback but the ones that keep a value.
/// </summary>
public abstract partial class KitFramePage : Component
{
    protected override Component? HeadAssets => [Markup.Title["kit frames"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    /// <summary>One component, where a gesture finds it.</summary>
    protected static Component Part(string name, params Component[] content) =>
        Markup.Div.Key(name).Data("c", name).Style("padding:16px;max-width:640px")[content];
}

/// <summary>What only shows something: a pointer and the keyboard have nothing to tell the server here.</summary>
public sealed partial class KitDisplayFramePage : KitFramePage
{
    private int _page = 2;

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("accordion", Ui.Accordion[Questions()]),
            Part("accordion-exclusive", Ui.Accordion.Exclusive().Transition()[Questions()]),
            Part("avatar", Ui.Avatar.Name("Ada Lovelace").Tooltip("Ada Lovelace"), Ui.Avatar.Name("Caleb Porzio").Href("#")),
            Part("badge", Ui.Badge["New"], Ui.Badge.Color(Ui.Color.Lime)[Span["Admin"], Ui.BadgeClose]),
            Part("breadcrumbs", Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href("#")["Home"], Ui.BreadcrumbsItem.Href("#")["Blog"], Ui.BreadcrumbsItem["Post"]]),
            Part("button", Ui.Button["Save"], Ui.Button.Primary.Tooltip("Publish it")["Publish"], Ui.Button.Loading(true)["Saving"]),
            Part("callout", Ui.Callout.Icon(Ui.IconName.Check).Heading("Saved").Text("Nothing else to do.")
                .Controls(Ui.Button.Ghost.Sm.Icon(Ui.IconName.XMark).AriaLabel("Close"))),
            Part("card", Ui.Card[Ui.Heading["Plan"], Ui.Text["Billed monthly."], Ui.Button["Change"]]),
            Part("kanban", Ui.Kanban[
                Ui.KanbanColumn.Key("planned")[
                    Ui.KanbanColumnHeader.Heading("Planned").Count(2),
                    Ui.KanbanColumnCards[Ui.KanbanCard.Key("a").Heading("Search bar"), Ui.KanbanCard.Key("b").Heading("Spinner").As(Ui.KanbanCardAs.Button)]
                ]
            ]),
            Part("pagination", Ui.Pagination.Paginator(new UiPaginator { Page = _page, PerPage = 5, Total = 40 }).OnPage(page => _page = page)),
            Part("progress", Ui.Progress.Value(40)),
            Part("separator", Ui.Separator, Ui.Separator.Text("or")),
            Part("skeleton", Ui.SkeletonGroup.Shimmer[Ui.SkeletonLine, Ui.SkeletonLine]),
            Part("table", Ui.Table[
                Ui.TableColumns[Ui.TableColumn["Customer"], Ui.TableColumn.Sortable().Sorted()["Date"], Ui.TableColumn.Sortable()["Amount"]],
                Ui.TableRows[
                    Ui.TableRow.Key("1")[Ui.TableCell["Lindsey"], Ui.TableCell["Jul 29"], Ui.TableCell["$49.00"]],
                    Ui.TableRow.Key("2")[Ui.TableCell["Hanna"], Ui.TableCell["Jul 28"], Ui.TableCell["$312.00"]]
                ]
            ]),
            Part("timeline", Ui.Timeline[
                Ui.TimelineItem[Ui.TimelineIndicator, Ui.TimelineContent[Ui.Heading["Order confirmed"]]],
                Ui.TimelineItem[Ui.TimelineIndicator, Ui.TimelineContent[Ui.Heading["Shipped"]]]
            ]),
            Part("tooltip",
                Ui.Tooltip.Key("plain").Content("Settings")[Ui.Button.Sm["Settings"]],
                Ui.Tooltip.Key("toggle").Toggleable()[Ui.Button.Sm.Ghost.Icon(Ui.IconName.InformationCircle), Ui.TooltipContent[P["Nine digits."]]],
                Ui.Tooltip.Key("interactive").Interactive().Content("Read more")[Ui.Button.Sm["More"]]),
            Part("profile", Ui.Profile.Name("Caleb Porzio"), Ui.Profile.Circle().Chevron(false).Initials("CP"))
        ];

    private static IEnumerable<Component> Questions() =>
    [
        Ui.AccordionItem.Key("refund").Heading("What's your refund policy?")["Thirty days."],
        Ui.AccordionItem.Key("bulk").Heading("Do you offer discounts?")["Yes, from ten."],
        Ui.AccordionItem.Key("track").Heading("How do I track my order?")["By the link in the mail."],
    ];
}

/// <summary>The controls that keep a value: only a value changing may be sent.</summary>
public sealed partial class KitFieldFramePage : KitFramePage
{
    private bool _agree;
    private bool _alerts;
    private string _plan = "free";
    private readonly Choices _choices = new();
    private string _name = "Ada";
    private string _secret = "hunter2";
    private string _phone = string.Empty;
    private string _notes = "Hello";
    private int _volume = 30;
    private string _code = string.Empty;

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("checkbox", Ui.Checkbox.Value(_agree).Label("Agree").OnChange(v => _agree = v)),
            Part("checkbox-group", Ui.CheckboxGroup.Bind(() => _choices.Channels).Live().Id("channels").Label("Channels")[
                Ui.Checkbox.Value("mail").Label("Mail"), Ui.Checkbox.Value("push").Label("Push"), Ui.Checkbox.Value("sms").Label("SMS")
            ]),
            Part("radio", Ui.RadioGroup.Value(_plan).Label("Plan").OnChange(v => _plan = v)[
                Ui.Radio.Value("free").Label("Free"), Ui.Radio.Value("pro").Label("Pro"), Ui.Radio.Value("team").Label("Team")
            ]),
            Part("radio-segmented", Ui.RadioGroup.Value(_plan).Segmented.Id("plan-seg").OnChange(v => _plan = v)[
                Ui.Radio.Value("free").Label("Free"), Ui.Radio.Value("pro").Label("Pro"), Ui.Radio.Value("team").Label("Team")
            ]),
            Part("switch", Ui.Switch.Value(_alerts).Label("Alerts").OnChange(v => _alerts = v)),
            Part("input", Ui.Input.Value(_name).Label("Name").Description("As on the card.").OnChange(v => _name = v)),
            Part("input-clearable", Ui.Input.Value(_name).Id("clearable").Clearable().OnChange(v => _name = v)),
            Part("input-copyable", Ui.Input.Value(_name).Id("copyable").Copyable().ReadOnly()),
            Part("input-viewable", Ui.Input.Value(_secret).Id("viewable").Type(InputType.Password).Viewable().OnChange(v => _secret = v)),
            Part("input-mask", Ui.Input.Value(_phone).Id("masked").Mask("(999) 999-9999").OnChange(v => _phone = v)),
            Part("textarea", Ui.Textarea.Value(_notes).Label("Notes").OnChange(v => _notes = v)),
            Part("field", Ui.Field[Ui.Label["Email"], Ui.Input.Value(_name).Id("in-field").OnChange(v => _name = v), Ui.Description["Never shared."]]),
            Part("slider", Ui.Slider.Value(_volume).OnChange(v => _volume = v)),
            Part("otp", Ui.Otp.Value(_code).Length(4).OnChange(v => _code = v)),
            Part("file-upload", Ui.FileUpload.Id("files").Label("Upload files").Multiple()[
                Ui.FileUploadDropzone.Heading("Drop files here or click to browse").Text("PNG up to 10MB")
            ])
        ];

    private sealed class Choices
    {
        public ICollection<string> Channels { get; set; } = ["mail"];
    }
}

/// <summary>What opens over the page: opening, moving about and closing are the browser's.</summary>
public sealed partial class KitOverlayFramePage : KitFramePage
{
    private bool _confirming;
    private bool _heard;
    private bool _drafts;
    private string _sort = "active";

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("dropdown", Ui.Dropdown[
                Ui.Button.Id("dropdown-open")["Options"],
                Ui.Menu[
                    Ui.MenuItem["New post"],
                    Ui.MenuSubmenu.Heading("Sort by")[Ui.MenuItem["Name"], Ui.MenuItem["Date"]],
                    Ui.MenuCheckbox.Value(_drafts).OnChange(on => _drafts = on)["Show drafts"],
                    Ui.MenuItem.Danger["Delete"]
                ]
            ]),
            Part("dropdown-hover", Ui.Dropdown.Hover()[Ui.Button.Id("hover-open")["Hover"], Ui.Menu[Ui.MenuItem["One"], Ui.MenuItem["Two"]]]),
            // The one dropdown on the page that asked to hear the reader open and close it.
            Part("dropdown-heard", Ui.Dropdown.OnToggle(open => _heard = open)[
                Ui.Button.Id("heard-open")[_heard ? "Open" : "Closed"],
                Ui.Menu[Ui.MenuItem["One"], Ui.MenuItem["Two"]]
            ]),
            Part("context", Ui.Context[
                Div.Id("context-area").Style("width:200px;height:60px;border:1px dashed gray")["Right click"],
                Ui.Menu[Ui.MenuItem["Copy"], Ui.MenuItem["Paste"]]
            ]),
            Part("popover", Ui.Dropdown[
                Ui.Button.Id("popover-open")["Filters"],
                Ui.Popover[
                    Ui.RadioGroup.Value(_sort).Id("popover-sort").Label("Sort by").OnChange(v => _sort = v)[
                        Ui.Radio.Value("active").Label("Recently active"), Ui.Radio.Value("posted").Label("Date posted")
                    ]
                ]
            ]),
            Part("popover-hover", Ui.Dropdown.Hover()[Ui.Button.Id("card-open")["Card"], Ui.Popover[P["Ada Lovelace"]]]),
            Part("navmenu", Ui.Dropdown[
                Ui.Button.Id("navmenu-open")["Account"],
                Ui.Navmenu[Ui.NavmenuItem.Key("a").Href("#account")["Account"], Ui.NavmenuItem.Key("b").Href("#billing")["Billing"]]
            ]),
            Part("modal",
                Ui.ModalTrigger.Name("edit")[Ui.Button.Id("modal-open")["Edit profile"]],
                Ui.Modal.Key("edit").Name("edit")[
                    Ui.Heading["Update profile"], Ui.Input.Value("Ada").Id("modal-name"), Ui.ModalClose[Ui.Button.Id("modal-cancel")["Cancel"]]
                ]),
            Part("modal-flyout",
                Ui.ModalTrigger.Name("fly")[Ui.Button.Id("flyout-open")["Open flyout"]],
                Ui.Modal.Key("fly").Name("fly").Flyout()[Ui.Heading["Filters"], Ui.Button["Apply"]]),
            Part("modal-state",
                Ui.Button.Id("state-open").OnClick(() => { _confirming = true; })["Delete order"],
                Ui.Modal.Key("state").Open(_confirming).OnClose(() => { _confirming = false; })[
                    Ui.Heading["Delete order"], Ui.ModalClose[Ui.Button.Id("state-cancel")["Cancel"]]
                ]),
            Part("command", Ui.Command.Label("Search commands")[
                Ui.MenuItem["New invoice"], Ui.MenuItem["Copy invoice link"], Ui.MenuItem["Export all"], Ui.MenuItem.Danger["Sign out"]
            ]),
            Part("toast", Ui.Button.Id("toast-raise").OnClick(() => Toast.Info("Your changes have been saved.").UntilDismissed())["Raise"]),
            Ui.ToastGroup[Ui.Toast]
        ];
}

/// <summary>What shows one view of several: which one is shown is the browser's to keep.</summary>
public sealed partial class KitNavigationFramePage : KitFramePage
{
    private readonly Shown _shown = new();
    private string _view = "list";

    private sealed class Shown
    {
        public string Panel { get; set; } = "profile";
    }

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("tabs", Ui.TabGroup[
                Ui.Tabs[Ui.Tab.Name("profile")["Profile"], Ui.Tab.Name("account")["Account"], Ui.Tab.Name("billing")["Billing"]],
                Ui.TabPanel.Name("profile")["Your name."],
                Ui.TabPanel.Name("account")["Your email."],
                Ui.TabPanel.Name("billing")["Your plan."]
            ]),
            Part("tabs-segmented", Ui.Tabs.Segmented[Ui.Tab.Name("list")["List"], Ui.Tab.Name("board")["Board"], Ui.Tab.Name("timeline")["Timeline"]]),
            Part("tabs-bound", Ui.TabGroup[
                Ui.Tabs.Bind(() => _shown.Panel)[Ui.Tab.Name("profile")["Profile"], Ui.Tab.Name("account")["Account"], Ui.Tab.Name("billing")["Billing"]],
                Ui.TabPanel.Name("profile")["Your name."],
                Ui.TabPanel.Name("account")["Your email."],
                Ui.TabPanel.Name("billing")["Your plan."]
            ]),
            Part("tabs-heard", Ui.Tabs.Segmented.Value(_view).OnChange(view => _view = view)[
                Ui.Tab.Name("list")["List"], Ui.Tab.Name("board")["Board"], Ui.Tab.Name("timeline")["Timeline"]
            ]),
            Part("navbar", Ui.Navbar[
                Ui.NavbarItem.Key("1").Href("#").Current(true)["Home"],
                Ui.NavbarItem.Key("2").Href("#").Icon(Ui.IconName.PuzzlePiece).Badge("12")["Features"],
                Ui.NavbarItem.Key("3").Href("#")["Pricing"]
            ]),
            Part("navlist", Ui.Navlist[
                Ui.NavlistItem.Key("1").Href("#").Icon(Ui.IconName.Home).Current(true)["Dashboard"],
                Ui.NavlistGroup.Key("g").Heading("Account").Expandable()[
                    Ui.NavlistItem.Key("g1").Href("#")["Profile"], Ui.NavlistItem.Key("g2").Href("#")["Settings"]
                ],
                Ui.NavlistGroup.Key("c").Heading("Team").Expandable().Expanded(false)[Ui.NavlistItem.Key("c1").Href("#")["Members"]]
            ]),
            Part("header", Ui.Header[
                Ui.Navbar[Ui.NavbarItem.Key("1").Href("#").Current(true)["Home"], Ui.NavbarItem.Key("2").Href("#")["About"]],
                Ui.Spacer,
                Ui.Profile.Initials("CP")
            ])
        ];
}

/// <summary>Flux's sidebar layout: collapsing it, folding a group and the rail's tooltips are the browser's.</summary>
public sealed partial class KitSidebarFramePage : KitFramePage
{
    protected override Component? Render() =>
        Div.Id("stage").Class("min-h-dvh")[
            Ui.Sidebar.Sticky().Collapsible(Ui.SidebarCollapsible.Always)[
                Ui.SidebarHeader[Ui.SidebarBrand.Href("#").Name("Rask"), Ui.SidebarCollapse],
                Ui.SidebarSearch.Placeholder("Search..."),
                Ui.SidebarNav[
                    Ui.SidebarItem.Icon(Ui.IconName.Home).Href("#").Tooltip("Home")["Home"],
                    Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#").Tooltip("Inbox")["Inbox"],
                    Ui.SidebarGroup.Expandable().Icon(Ui.IconName.Star).Heading("Favorites")[
                        Ui.SidebarItem.Href("#")["Actions"], Ui.SidebarItem.Href("#")["Navigation"]
                    ]
                ],
                Ui.SidebarSpacer,
                Ui.SidebarProfile.Name("Ada Lovelace")
            ],
            Ui.Header.Class("lg:hidden")[Ui.SidebarToggle.Inset(Ui.Position.Left), Ui.Spacer],
            Ui.Main[H1["Good afternoon"]]
        ];
}
