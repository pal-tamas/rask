using FluentValidation;

namespace Rask.Example.Shared.Features;

// FluentValidation with SetValidator + RuleForEach — one root validator covers the whole
// graph; Rask routes dotted property paths back to the runtime sub-instance.
public sealed partial class NestedFluentValidationDemo : Component
{
    private readonly NestedOrderModel _model = new();
    private readonly NestedOrderValidator _validator = new();
    private int _seq = 2;
    private string? _submission;

    public NestedFluentValidationDemo() => _model.Lines.Add(new NestedOrderLine { Sku = "BOX-1", Quantity = 3 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
        foreach (var line in _model.Lines)
        {
            var captured = line;
            rows.Add(Tr.Key(captured.Id)[
                Td[
                    Input.Bind(() => captured.Sku).Class("form-control form-control-sm"),
                    ValidationMessage.Template(FieldError).For(() => captured.Sku)
                ],
                Td.Style("width: 6rem;")[
                    Input.Bind(() => captured.Quantity).Class("form-control form-control-sm"),
                    ValidationMessage.Template(FieldError).For(() => captured.Quantity)
                ],
                Td.Style("width: 3rem;")[
                    Button.Type("button").Class(Ui.BtnOutlineDanger)
                        .OnClick(() => _model.Lines.Remove(captured))[Icon.Name(IconName.XLg)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnValidSubmit(m => _submission = $"Order routed: {m.CustomerName} → {m.Address.Street}, {m.Lines.Count} line(s)").Class("vstack gap-3")[
                FluentValidationValidator.Validator(_validator),
                Div[
                    Label.For("nf-fv-name").Class("form-label small mb-1")["Customer"],
                    Input.Bind(() => _model.CustomerName).Id("nf-fv-name").Class("form-control"),
                    ValidationMessage.Template(FieldError).For(() => _model.CustomerName)
                ],
                Fieldset.Class("border rounded p-3")[
                    Legend.Class("h6 fw-semibold")["Address"],
                    Div.Class("vstack gap-2")[
                        Div[
                            Input.Bind(() => _model.Address.Street).Class("form-control"),
                            ValidationMessage.Template(FieldError).For(() => _model.Address.Street)
                        ],
                        Div[
                            Input.Bind(() => _model.Address.City).Class("form-control"),
                            ValidationMessage.Template(FieldError).For(() => _model.Address.City)
                        ]
                    ]
                ],
                Table.Class("table table-sm align-middle mb-0 mt-2")[
                    Thead[Tr[Th["SKU"], Th["Qty"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Button.Type("button").Class(Ui.BtnOutlineSecondary)
                        .Id("nf-fv-add")
                        .OnClick(() => _model.Lines.Add(new NestedOrderLine { Sku = $"BOX-{_seq++}", Quantity = 1 }))[
                        Icon.Name(IconName.PlusLg).Class("me-1"), "Add line"],
                    Button.Class(Ui.BtnPrimary).Type("submit").Id("nf-fv-submit")[
                        Icon.Name(IconName.Check2Circle).Class("me-1"), "Place"]
                ]
            ],
            _submission is null
                ? null
                : Div.Class($"{Ui.AlertSuccess} small mt-3 mb-0").Id("nf-fv-result")[_submission]
        ];
    }
}

public sealed class NestedOrderModel
{
    public string CustomerName { get; set; } = "";
    public NestedOrderAddress Address { get; set; } = new();
    public List<NestedOrderLine> Lines { get; set; } = new();
}

public sealed class NestedOrderAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class NestedOrderLine
{
    // Stable per-instance key for keyed row diffing.
    public Guid Id { get; } = Guid.NewGuid();

    public string Sku { get; set; } = "";
    public int Quantity { get; set; } = 1;
}

public sealed class NestedOrderValidator : AbstractValidator<NestedOrderModel>
{
    public NestedOrderValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Customer name is required.");
        RuleFor(x => x.Address).SetValidator(new NestedOrderAddressValidator());
        RuleForEach(x => x.Lines).SetValidator(new NestedOrderLineValidator());
    }
}

public sealed class NestedOrderAddressValidator : AbstractValidator<NestedOrderAddress>
{
    public NestedOrderAddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty().WithMessage("Street is required.");
        RuleFor(x => x.City).NotEmpty().WithMessage("City is required.");
    }
}

public sealed class NestedOrderLineValidator : AbstractValidator<NestedOrderLine>
{
    public NestedOrderLineValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("SKU is required.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be positive.");
    }
}
