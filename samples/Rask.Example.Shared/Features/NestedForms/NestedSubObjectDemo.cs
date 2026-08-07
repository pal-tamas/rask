using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Sub-object binding — sub-class instance owns its own validation state under a single
// top-of-form DataAnnotationsValidator.
public sealed partial class NestedSubObjectDemo : Component
{
    private readonly CheckoutModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override Component? Render() =>
    [
        Form<CheckoutModel>(
            _model,
            m => _submission =
                $"Checked out as {m.Name} to {m.Address.Street}, {m.Address.City} ({m.Address.Country}).",
            Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            Div()[
                Label("nf-name", Class: "form-label small mb-1")["Name"],
                Input(() => _model.Name, Id: "nf-name", Class: "form-control"),
                ValidationMessage(() => _model.Name, FieldError)
            ],
            Div()[
                Label("nf-email", Class: "form-label small mb-1")["Email"],
                Input(() => _model.Email, Id: "nf-email", Type: InputType.Email,
                    Class: "form-control"),
                ValidationMessage(() => _model.Email, FieldError)
            ],
            Fieldset(Class: "border rounded p-3 mt-2")[
                Legend(Class: "h6 fw-semibold")["Shipping address"],
                Div(Class: "vstack gap-3")[
                    Div()[
                        Label("nf-street", Class: "form-label small mb-1")["Street"],
                        Input(() => _model.Address.Street, Id: "nf-street",
                            Class: "form-control"),
                        ValidationMessage(() => _model.Address.Street, FieldError)
                    ],
                    Div()[
                        Label("nf-city", Class: "form-label small mb-1")["City"],
                        Input(() => _model.Address.City, Id: "nf-city",
                            Class: "form-control"),
                        ValidationMessage(() => _model.Address.City, FieldError)
                    ],
                    Div()[
                        Label("nf-country", Class: "form-label small mb-1")["Country (ISO)"],
                        Input(() => _model.Address.Country, Id: "nf-country",
                            Class: "form-control", MaxLength: 2),
                        ValidationMessage(() => _model.Address.Country, FieldError)
                    ]
                ]
            ],
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary, Id: "nf-submit")[
                    BsIcon(Name: BsIconName.Check2Circle, Class: "me-1"), "Place order"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0", Id: "nf-result")[
                BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];
}

public sealed class CheckoutModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(60)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Looks like an invalid email.")]
    public string Email { get; set; } = "";

    public AddressModel Address { get; set; } = new();
}

public sealed class AddressModel
{
    [Required(ErrorMessage = "Street is required.")]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "City is required.")]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Country is required.")]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "Use the ISO 2-letter code.")]
    public string Country { get; set; } = "";
}
