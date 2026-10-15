namespace Rask.Site.Features.UiKit;

// Flux's phone field, example by example: the number formatted as it is typed, the country picked from a
// searchable list of flags, and one value — E.164 once the number is possible — bound to the model.
public sealed partial class UiKitDataInputDemo
{
    private static readonly string[] Four = ["US", "CA", "GB", "AU"];

    private readonly Contact _contact = new();
    private string? _savedPhone;

    private Component PhoneSection() =>
        Section(
            "Phone — Flux UI's, example by example",
            "intl-tel-input, the library Flux's field runs, formats the number as it is typed and follows a number "
            + "typed with its calling code to its country; the list searches every country by name and code. "
            + "Typing and the list send the page nothing: the number, E.164 once it is a possible one, goes with the "
            + "next action. A number not yet complete is kept as typed.",
            Div.Data(Testid("ui-phone")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Div.Key("bound").Class("space-y-2")[
                    Ui.Phone.Bind(() => _contact.Mobile).Country("US").Label("Phone number").Placeholder("(201) 555-0123"),
                    Div.Class("flex items-center gap-3")[
                        Ui.Button.Sm.Data(Testid("ui-phone-save")).OnClick(() => { _savedPhone = _contact.Mobile; })["Save"],
                        P.Class("text-sm text-ui-muted").Data(Testid("ui-phone-saved"))[
                            Saved()
                        ]
                    ]
                ],
                Ui.Phone.Bind(() => _contact.Office).Key("office").Country("GB").Label("Office phone"),
                Ui.Phone.Bind(() => _contact.Support).Key("support").Country("US").Countries(Four).Label("Support phone"),
                Ui.Phone.Bind(() => _contact.Other).Key("contact").Country("US").CountryOrder(Four).Label("Contact phone"),
                Ui.Phone.Value("+12015550123").Key("filled").Country("US").Filled.Label("Mobile phone"),
                Div.Key("sizes").Class("space-y-4")[
                    Ui.Phone.Value("+12015550123").Key("sm").Country("US").Sm,
                    Ui.Phone.Value("+12015550123").Key("xs").Country("US").Xs
                ],
                Ui.Phone.Value("+12015550123").Key("disabled").Country("US").Disabled().Label("Disabled phone"),
                Ui.Phone.Value("+12015550123").Key("readonly").Country("US").ReadOnly().Label("Readonly phone"),
                Ui.Phone.Value("201").Key("invalid").Id("invalid-phone").Country("US").Invalid().Label("Phone number")
                    .DescriptionTrailing("Enter a complete phone number.")
            ]);

    private string Saved()
    {
        if (_savedPhone is null)
        {
            return "Nothing saved yet.";
        }

        return _savedPhone.Length == 0 ? "Saved: (empty)" : $"Saved: {_savedPhone}";
    }

    // What the examples bind to: strings, as a form model has them, empty until the reader types.
    public sealed class Contact
    {
        public string? Mobile { get; set; }

        public string? Office { get; set; } = "+442071838750";

        public string? Support { get; set; }

        public string? Other { get; set; }
    }
}
