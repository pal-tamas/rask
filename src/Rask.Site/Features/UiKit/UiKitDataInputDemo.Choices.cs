namespace Rask.Site.Features.UiKit;

// Flux UI's checkbox, radio and switch, example by example: every one of them bound to the model below,
// so two examples over the same member move together.
public sealed partial class UiKitDataInputDemo
{
    private readonly Choices _choices = new();

    private Component CheckboxSection() =>
        Section(
            "Checkbox — Flux UI's, example by example",
            "On its own a checkbox binds a bool. In a group the GROUP binds the collection your model declares and "
            + "each checkbox's Value is what the collection holds while it is ticked; the group's variant draws the "
            + "same checkboxes as cards, pills or buttons. Every one is a real input in a label, so the space bar "
            + "and the form post are the browser's.",
            Div.Data(Testid("ui-checkbox")).Class("grid max-w-4xl items-start gap-8 sm:grid-cols-2")[
                CheckboxLists(),
                CheckboxVariants(),
                P.Key("state").Class("text-sm text-zinc-500 sm:col-span-2 dark:text-white/60").Data(Testid("ui-checkbox-state"))[
                    $"terms {(_choices.Terms ? "agreed" : "open")} · notify by {Listed(_choices.Notifications)}"
                    + $" · subscribed to {Listed(_choices.Subscription)} · people {Listed(_choices.People)}"
                ]
            ]);

    private Component[] CheckboxLists() =>
    [
        Ui.Field.Key("terms").Variant(Ui.FieldVariant.Inline)[
            Ui.Checkbox.Bind(() => _choices.Terms).Id("cb-terms"),
            Ui.Label["I agree to the terms and conditions"],
            Ui.Error
        ],
        Div.Key("single").Class("space-y-3")[
            Ui.Checkbox.Id("cb-checked").Checked().Label("Enable notifications"),
            Ui.Checkbox.Id("cb-disabled").Disabled().Label("Read and write")
        ],
        Ui.CheckboxGroup.Bind(() => _choices.Notifications).Key("group").Id("cb-notifications").Label("Notifications")[
            Ui.Checkbox.Value("push").Label("Push notifications"),
            Ui.Checkbox.Value("email").Label("Email"),
            Ui.Checkbox.Value("app").Label("In-app alerts"),
            Ui.Checkbox.Value("sms").Label("SMS")
        ],
        Ui.CheckboxGroup.Bind(() => _choices.Subscription).Key("described").Id("cb-described")
            .Label("Subscription preferences")[
            Ui.Checkbox.Value("newsletter").Label("Newsletter")
                .Description("Receive our monthly newsletter with the latest updates and offers."),
            Ui.Checkbox.Value("updates").Label("Product updates")
                .Description("Stay informed about new features and product updates."),
            Ui.Checkbox.Value("invitations").Label("Event invitations")
                .Description("Get invitations to our exclusive events and webinars.")
        ],
        Ui.Fieldset.Key("languages").Legend("Languages").Description("Choose the languages you want to support.")[
            Div.Class("flex flex-wrap gap-4")[
                Ui.Checkbox.Bind(() => _choices.English).Id("cb-english").Label("English"),
                Ui.Checkbox.Bind(() => _choices.Spanish).Id("cb-spanish").Label("Spanish"),
                Ui.Checkbox.Bind(() => _choices.French).Id("cb-french").Label("French"),
                Ui.Checkbox.Bind(() => _choices.German).Id("cb-german").Label("German")
            ]
        ],
        Ui.CheckboxGroup.Bind(() => _choices.People).Key("all").Id("cb-people")[
            Table.Class("w-full max-w-64 text-sm")[
                Thead[Tr.Class("border-b border-zinc-800/10 dark:border-white/20")[
                    Th.Class("w-10 py-3 ps-1")[Ui.CheckboxAll],
                    Th.Class("py-3 text-start font-medium")["Name"]
                ]],
                Tbody[
                    Person("caleb", "Caleb Porzio"),
                    Person("hugo", "Hugo Sainte-Marie"),
                    Person("keith", "Keith Damiani")
                ]
            ]
        ],
    ];

    private Component[] CheckboxVariants() =>
    [
        Wide("cards", Cards("cards").Class("max-sm:flex-col")[
            Ui.Checkbox.Value("newsletter").Label("Newsletter").Description("Get the latest updates and offers."),
            Ui.Checkbox.Value("updates").Label("Product updates").Description("Learn about new features and products."),
            Ui.Checkbox.Value("invitations").Label("Event invitations").Description("Invitations to exclusive events.")
        ]),
        Cards("vertical").Key("vertical").Class("flex-col")[
            Ui.Checkbox.Value("newsletter").Label("Newsletter").Description("Get the latest updates and offers."),
            Ui.Checkbox.Value("updates").Label("Product updates").Description("Learn about new features and products."),
            Ui.Checkbox.Value("invitations").Label("Event invitations").Description("Invitations to exclusive events.")
        ],
        Cards("icons").Key("icons").Class("flex-col")[
            Ui.Checkbox.Value("newsletter").Icon(Ui.IconName.Newspaper).Label("Newsletter")
                .Description("Get the latest updates and offers."),
            Ui.Checkbox.Value("updates").Icon(Ui.IconName.Cube).Label("Product updates")
                .Description("Learn about new features and products."),
            Ui.Checkbox.Value("invitations").Icon(Ui.IconName.Calendar).Label("Event invitations")
                .Description("Invitations to exclusive events.")
        ],
        Cards("custom").Key("custom").Class("flex-col")[
            CustomCheckbox("newsletter", "Newsletter", "Get the latest updates and offers."),
            CustomCheckbox("updates", "Product updates", "Learn about new features and products."),
            CustomCheckbox("invitations", "Event invitations", "Invitations to exclusive events.")
        ],
        Div.Key("tags").Class("space-y-8")[
            Ui.CheckboxGroup.Bind(() => _choices.Categories).Id("cb-categories").Label("Categories")
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
            ],
            Ui.CheckboxGroup.Bind(() => _choices.Features).Id("cb-features").Label("Features")
                .Variant(Ui.CheckboxGroupVariant.Buttons).Class("flex-wrap")[
                Ui.Checkbox.Value("notifications").Icon(Ui.IconName.Bell).Label("Notifications"),
                Ui.Checkbox.Value("analytics").Icon(Ui.IconName.ChartBar).Label("Analytics"),
                Ui.Checkbox.Value("backups").Icon(Ui.IconName.CloudArrowUp).Label("Backups")
            ]
        ],
    ];

    private Component RadioSection() =>
        Section(
            "Radio — Flux UI's, example by example",
            "A radio group binds ONE value, of any type; the radios are its children and each one's Value is what the "
            + "member becomes when it is chosen. Segmented, cards, pills and buttons are the same radios drawn "
            + "differently: the arrow keys move and choose in every one of them, because each holds a real input "
            + "that shares the group's name.",
            Div.Data(Testid("ui-radio")).Class("grid max-w-4xl items-start gap-8 sm:grid-cols-2")[
                Ui.RadioGroup.Bind(() => _choices.Payment).Key("payment").Id("rd-payment").Label("Select your payment method")[
                    Ui.Radio.Value("cc").Label("Credit Card"),
                    Ui.Radio.Value("paypal").Label("Paypal"),
                    Ui.Radio.Value("ach").Label("Bank transfer")
                ],
                Ui.RadioGroup.Bind(() => _choices.Role).Key("described").Id("rd-described").Label("Role")[Roles()],
                Ui.Fieldset.Key("fieldset").Legend("Role")[
                    Ui.RadioGroup.Bind(() => _choices.Role).Id("rd-fieldset")[Roles()]
                ],
                Div.Key("segmented").Class("space-y-8")[
                    Ui.RadioGroup.Bind(() => _choices.Role).Id("rd-segmented").Label("Role").Segmented[Segments(icons: false)],
                    Ui.RadioGroup.Bind(() => _choices.Role).Id("rd-segmented-sm").Label("Role").Segmented.Sm[Segments(icons: false)],
                    Ui.RadioGroup.Bind(() => _choices.Role).Id("rd-segmented-icons").Label("Role").Segmented[Segments(icons: true)]
                ],
                Wide("cards", Shipping("rd-cards").Class("max-sm:flex-col")[ShippingCards(icons: false)]),
                Shipping("rd-vertical").Key("vertical").Class("flex-col")[ShippingCards(icons: false)],
                Shipping("rd-custom").Key("custom").Class("flex-col")[
                    CustomRadio("standard", "Standard", "4-10 business days"),
                    CustomRadio("fast", "Fast", "2-5 business days"),
                    CustomRadio("next-day", "Next day", "1 business day")
                ],
                Wide("icons", Shipping("rd-icons").Class("max-sm:flex-col")[ShippingCards(icons: true)]),
                Wide("bare", Shipping("rd-bare").Indicator(false).Class("max-sm:flex-col")[ShippingCards(icons: true)]),
                Ui.RadioGroup.Bind(() => _choices.Priority).Key("pills").Id("rd-priority").Label("Priority").Pills[
                    Ui.Radio.Value("low").Label("Low"),
                    Ui.Radio.Value("medium").Label("Medium"),
                    Ui.Radio.Value("high").Label("High"),
                    Ui.Radio.Value("critical").Label("Critical")
                ],
                Ui.RadioGroup.Bind(() => _choices.Feedback).Key("buttons").Id("rd-feedback").Label("Feedback type").Buttons
                    .Class("w-full flex-wrap")[
                    Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt).Class("flex-1")["Bug report"],
                    Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb).Class("flex-1")["Suggestion"],
                    Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle).Class("flex-1")["Question"]
                ],
                P.Key("state").Class("text-sm text-zinc-500 sm:col-span-2 dark:text-white/60").Data(Testid("ui-radio-state"))[
                    $"paying by {_choices.Payment} · {_choices.Role} · shipping {_choices.Shipping}"
                    + $" · {_choices.Priority} priority · {_choices.Feedback}"
                ]
            ]);

    private Component SwitchSection() =>
        Section(
            "Switch — Flux UI's",
            "Turns a setting on now, where a checkbox states a fact a form submits later. It binds a bool, sits at "
            + "the far edge of its row, and Left puts it before its label. The thumb moves on the input's own "
            + ":checked, 150ms, with no script.",
            Div.Data(Testid("ui-switch")).Class("grid max-w-4xl items-start gap-8 sm:grid-cols-2")[
                Div.Key("inline").Class("sm:col-span-2")[
                    Ui.Field.Variant(Ui.FieldVariant.Inline).Class("max-w-xs")[
                        Ui.Label["Enable notifications"],
                        Ui.Switch.Bind(() => _choices.Notify).Id("sw-notify"),
                        Ui.Error
                    ]
                ],
                Ui.Fieldset.Key("fieldset").Legend("Email notifications")[
                    Div.Class("space-y-4")[
                        Ui.Switch.Bind(() => _choices.Communication).Id("sw-communication").Label("Communication emails")
                            .Description("Receive emails about your account activity."),
                        Rule(),
                        Ui.Switch.Bind(() => _choices.Marketing).Id("sw-marketing").Label("Marketing emails")
                            .Description("Receive emails about new products, features, and more."),
                        Rule(),
                        Ui.Switch.Bind(() => _choices.Social).Id("sw-social").Label("Social emails")
                            .Description("Receive emails for friend requests, follows, and more."),
                        Rule(),
                        Ui.Switch.Bind(() => _choices.Security).Id("sw-security").Label("Security emails")
                            .Description("Receive emails about your account activity and security.")
                    ]
                ],
                Ui.Fieldset.Key("left").Legend("Email notifications")[
                    Div.Class("space-y-3")[
                        Ui.Switch.Bind(() => _choices.Communication).Id("sw-left-communication").Label("Communication emails").Left,
                        Ui.Switch.Bind(() => _choices.Marketing).Id("sw-left-marketing").Label("Marketing emails").Left,
                        Ui.Switch.Bind(() => _choices.Social).Id("sw-left-social").Label("Social emails").Left,
                        Ui.Switch.Value(true).Id("sw-left-security").Label("Security emails").Left.Disabled()
                    ]
                ],
                P.Key("state").Class("text-sm text-zinc-500 sm:col-span-2 dark:text-white/60").Data(Testid("ui-switch-state"))[
                    $"notifications {(_choices.Notify ? "on" : "off")} · communication {(_choices.Communication ? "on" : "off")}"
                    + $" · marketing {(_choices.Marketing ? "on" : "off")}"
                ]
            ]);

    private UiCheckboxGroup<string> Cards(string id) =>
        Ui.CheckboxGroup.Bind(() => _choices.Subscription).Id("cb-" + id).Label("Subscription preferences")
            .Variant(Ui.CheckboxGroupVariant.Cards);

    private UiRadioGroup<string> Shipping(string id) =>
        Ui.RadioGroup.Bind(() => _choices.Shipping).Id(id).Label("Shipping").Cards;

    // The field a group draws is the grid's item, so a group that wants the whole row is wrapped to say so.
    private static Component Wide(string key, Component group) => Div.Key(key).Class("sm:col-span-2")[group];

    private static Component Person(string value, string name) =>
        Tr.Class("border-b border-zinc-800/10 last:border-0 dark:border-white/20")[
            Td.Class("py-3 ps-1")[Ui.Checkbox.Value(value)],
            Td.Class("py-3 text-zinc-500 dark:text-zinc-300")[name]
        ];

    private static Component CustomCheckbox(string value, string heading, string text) =>
        Ui.Checkbox.Value(value)[Ui.CheckboxIndicator, CardWords(heading, text)];

    private static Component CustomRadio(string value, string heading, string text) =>
        Ui.Radio.Value(value)[Ui.RadioIndicator, CardWords(heading, text)];

    // Flux's heading and text, which are not rebuilt yet: the two lines a custom card is given.
    private static Component CardWords(string heading, string text) =>
        Div.Class("flex-1")[
            Div.Class("text-sm leading-4 font-medium text-zinc-800 dark:text-white")[heading],
            P.Class("mt-2 text-xs text-zinc-500 dark:text-white/70")[text]
        ];

    // Flux's subtle separator, which is not rebuilt yet.
    private static HTMLDivElement Rule() => Div.Class("h-px bg-zinc-800/5 dark:bg-white/10").Role("none");

    private static Component[] Roles() =>
    [
        Ui.Radio.Value("administrator").Label("Administrator").Description("Administrator users can perform any action."),
        Ui.Radio.Value("editor").Label("Editor").Description("Editor users have the ability to read, create, and update."),
        Ui.Radio.Value("viewer").Label("Viewer")
            .Description("Viewer users only have the ability to read. Create, and update are restricted."),
    ];

    private static Component[] Segments(bool icons) =>
    [
        Ui.Radio.Value("administrator").Label("Admin").Icon(icons ? Ui.IconName.Wrench : null),
        Ui.Radio.Value("editor").Label("Editor").Icon(icons ? Ui.IconName.PencilSquare : null),
        Ui.Radio.Value("viewer").Label("Viewer").Icon(icons ? Ui.IconName.Eye : null),
    ];

    private static Component[] ShippingCards(bool icons) =>
    [
        Ui.Radio.Value("standard").Icon(icons ? Ui.IconName.Truck : null).Label("Standard").Description("4-10 business days"),
        Ui.Radio.Value("fast").Icon(icons ? Ui.IconName.Cube : null).Label("Fast").Description("2-5 business days"),
        Ui.Radio.Value("next-day").Icon(icons ? Ui.IconName.Clock : null).Label("Next day").Description("1 business day"),
    ];

    private static string Listed(ICollection<string> values) => values.Count == 0 ? "nothing" : string.Join(", ", values);

    // An ordinary model: the groups bind its collections and its strings, the checkboxes and switches its bools.
    public sealed class Choices
    {
        public bool Terms { get; set; }

        public ICollection<string> Notifications { get; set; } = ["push", "email"];

        public ICollection<string> Subscription { get; set; } = ["newsletter"];

        public bool English { get; set; } = true;

        public bool Spanish { get; set; } = true;

        public bool French { get; set; }

        public bool German { get; set; }

        public ICollection<string> People { get; set; } = ["caleb"];

        public ICollection<string> Categories { get; set; } = ["fantasy", "science-fiction", "thriller"];

        public ICollection<string> Features { get; set; } = ["notifications", "analytics"];

        public string Payment { get; set; } = "cc";

        public string Role { get; set; } = "administrator";

        public string Shipping { get; set; } = "standard";

        public string Priority { get; set; } = "medium";

        public string Feedback { get; set; } = "bug";

        public bool Notify { get; set; }

        public bool Communication { get; set; } = true;

        public bool Marketing { get; set; }

        public bool Social { get; set; }

        public bool Security { get; set; } = true;
    }
}
