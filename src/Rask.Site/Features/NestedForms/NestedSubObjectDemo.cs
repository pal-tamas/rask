using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

// Sub-object binding — sub-class instance owns its own validation state under a single
// the form's built-in validation, with nothing declared.
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
            Div[
                Ui.Input.Bind(() => _model.Name).Label("Name").Id("nf-name").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Ui.Input.Bind(() => _model.Email).Label("Email")
                    .Id("nf-email")
                    .Type(InputType.Email).ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Email)
            ],
            Fieldset.Class("border rounded p-3 mt-2")[
                Legend.Class("text-base font-semibold")["Shipping address"],
                Div.Class("flex flex-col gap-3")[
                    Div[
                        Ui.Input.Bind(() => _model.Address.Street).Label("Street")
                            .Id("nf-street").ShowValidation(false),
                        Validation.Message.Template(FieldError).For(() => _model.Address.Street)
                    ],
                    Div[
                        Ui.Input.Bind(() => _model.Address.City).Label("City")
                            .Id("nf-city").ShowValidation(false),
                        Validation.Message.Template(FieldError).For(() => _model.Address.City)
                    ],
                    Div[
                        Ui.Input.Bind(() => _model.Address.Country).Label("Country (ISO)")
                            .Id("nf-country")
                            .MaxLength(2).ShowValidation(false),
                        Validation.Message.Template(FieldError).For(() => _model.Address.Country)
                    ]
                ]
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit).Id("nf-submit")[Ui.Icon.Name(Ui.IconName.CheckCircle), "Place order"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0").Id("nf-result")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
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
