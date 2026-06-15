using FluentValidation;

namespace Rask.Example.Shared.Features;

// FluentValidation with SetValidator + RuleForEach — one root validator covers the whole
// graph; Rask routes dotted property paths back to the runtime sub-instance.
public sealed class NestedFluentValidationDemo : Component
{
    private readonly NestedOrderModel _model = new();
    private readonly NestedOrderValidator _validator = new();
    private int _seq = 2;
    private string? _submission;

    public NestedFluentValidationDemo() => _model.Lines.Add(new NestedOrderLine { Sku = "BOX-1", Quantity = 3 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render()
    {
        var rows = new List<Child>();
        foreach (var line in _model.Lines)
        {
            var captured = line;
            rows.Add(Tr(Key: captured.Id)[
                Td()[
                    Input(() => captured.Sku, Class: "form-control form-control-sm"),
                    ValidationMessage(() => captured.Sku, FieldError)
                ],
                Td(Style: "width: 6rem;")[
                    Input(() => captured.Quantity, Class: "form-control form-control-sm"),
                    ValidationMessage(() => captured.Quantity, FieldError)
                ],
                Td(Style: "width: 3rem;")[
                    Button("button",
                        Class: "btn btn-outline-danger btn-sm",
                        OnClick: () => _model.Lines.Remove(captured))[I(Class: "bi bi-x-lg")]
                ]
            ]);
        }

        return
        [
            Form<NestedOrderModel>(
                _model,
                m => _submission = $"Order routed: {m.CustomerName} → {m.Address.Street}, {m.Lines.Count} line(s)",
                Class: "vstack gap-3")[
                FluentValidationValidator(_validator),
                Div()[
                    Label("nf-fv-name", Class: "form-label small mb-1")["Customer"],
                    Input(() => _model.CustomerName, Id: "nf-fv-name", Class: "form-control"),
                    ValidationMessage(() => _model.CustomerName, FieldError)
                ],
                Fieldset(Class: "border rounded p-3")[
                    Legend(Class: "h6 fw-semibold")["Address"],
                    Div(Class: "vstack gap-2")[
                        Div()[
                            Input(() => _model.Address.Street, Class: "form-control"),
                            ValidationMessage(() => _model.Address.Street, FieldError)
                        ],
                        Div()[
                            Input(() => _model.Address.City, Class: "form-control"),
                            ValidationMessage(() => _model.Address.City, FieldError)
                        ]
                    ]
                ],
                Table(Class: "table table-sm align-middle mb-0 mt-2")[
                    Thead()[Tr()[Th()["SKU"], Th()["Qty"], Th()]],
                    Tbody()[rows]
                ],
                Div(Class: "d-flex gap-2")[
                    Button("button",
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "nf-fv-add",
                        OnClick: () => _model.Lines.Add(new NestedOrderLine { Sku = $"BOX-{_seq++}", Quantity = 1 }))[
                        I(Class: "bi bi-plus-lg me-1"), "Add line"],
                    Button("submit", Class: "btn btn-primary btn-sm", Id: "nf-fv-submit")[
                        I(Class: "bi bi-check2-circle me-1"), "Place"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0", Id: "nf-fv-result")[_submission]
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
