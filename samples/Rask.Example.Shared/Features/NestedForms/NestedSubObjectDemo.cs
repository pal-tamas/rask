using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Sub-object binding — sub-class instance owns its own validation state under a single
// top-of-form DataAnnotationsValidator.
public sealed partial class NestedSubObjectDemo : Component
{
    private readonly CheckoutModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission =
                $"Checked out as {m.Name} to {m.Address.Street}, {m.Address.City} ({m.Address.Country}).").Class("flex flex-col gap-3")[
            DataAnnotationsValidator,
            Div[
                Label.For("nf-name").Class($"{Ui.Label} text-sm mb-1")["Name"],
                Input.Bind(() => _model.Name).Id("nf-name").Class(Ui.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Label.For("nf-email").Class($"{Ui.Label} text-sm mb-1")["Email"],
                Input.Bind(() => _model.Email)
                    .Id("nf-email")
                    .Type(InputType.Email)
                    .Class(Ui.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Fieldset.Class("border rounded p-3 mt-2")[
                Legend.Class("text-base font-semibold")["Shipping address"],
                Div.Class("flex flex-col gap-3")[
                    Div[
                        Label.For("nf-street").Class($"{Ui.Label} text-sm mb-1")["Street"],
                        Input.Bind(() => _model.Address.Street)
                            .Id("nf-street")
                            .Class(Ui.Input),
                        ValidationMessage.Template(FieldError).For(() => _model.Address.Street)
                    ],
                    Div[
                        Label.For("nf-city").Class($"{Ui.Label} text-sm mb-1")["City"],
                        Input.Bind(() => _model.Address.City)
                            .Id("nf-city")
                            .Class(Ui.Input),
                        ValidationMessage.Template(FieldError).For(() => _model.Address.City)
                    ],
                    Div[
                        Label.For("nf-country").Class($"{Ui.Label} text-sm mb-1")["Country (ISO)"],
                        Input.Bind(() => _model.Address.Country)
                            .Id("nf-country")
                            .Class(Ui.Input)
                            .MaxLength(2),
                        ValidationMessage.Template(FieldError).For(() => _model.Address.Country)
                    ]
                ]
            ],
            Div[
                Button.Class(Ui.BtnPrimary).Type("submit").Id("nf-submit")[
                    Icon.Name(IconName.Check2Circle).Class("me-1"), "Place order"]
            ]
        ],
        _submission is null
            ? null
            : Div.Class($"{Ui.AlertSuccess} text-sm mt-3 mb-0").Id("nf-result")[
                Icon.Name(IconName.CheckCircle).Class("me-2"), _submission]
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
