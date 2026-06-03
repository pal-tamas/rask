using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace Rask.Example.Shared.Demos;

// Sub-object binding — sub-class instance owns its own validation state under a single
// top-of-form DataAnnotationsValidator.
public sealed class NestedSubObjectDemo : Component
{
    private readonly CheckoutModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
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
                    Input(() => _model.Email, Id: "nf-email", Type: "email",
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
                    Button("submit", Class: "btn btn-primary", Id: "nf-submit")[
                        I(Class: "bi bi-check2-circle me-1"), "Place order"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0", Id: "nf-result")[
                    I(Class: "bi bi-check-circle me-2"), _submission]];
}

// Collection binding via foreach-capture — the canonical pattern.
public sealed class NestedListForeachDemo : Component
{
    private readonly CartModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedListForeachDemo() =>
        _model.Items.Add(new LineItem { Description = "Coffee beans (250g)", Quantity = 2 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render()
    {
        var rows = new List<Child>();
        foreach (var item in _model.Items)
        {
            var captured = item; // foreach already captures per-iteration but make it loud.
            rows.Add(Tr(Key: captured.Id)[
                Td()[
                    Input(() => captured.Description, Class: "form-control form-control-sm"),
                    ValidationMessage(() => captured.Description, FieldError)
                ],
                Td(Style: "width: 6rem;")[
                    Input(() => captured.Quantity, Class: "form-control form-control-sm"),
                    ValidationMessage(() => captured.Quantity, FieldError)
                ],
                Td(Style: "width: 3rem;")[
                    Button("button",
                        Class: "btn btn-outline-danger btn-sm",
                        OnClick: () => _model.Items.Remove(captured))[I(Class: "bi bi-x-lg")]
                ]
            ]);
        }

        return [
            Form<CartModel>(
                _model,
                m => _submission = $"Submitted {m.Items.Count} line item(s).",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Table(Class: "table table-sm align-middle mb-0")[
                    Thead()[Tr()[Th()["Description"], Th()["Quantity"], Th()]],
                    Tbody()[rows]
                ],
                Div(Class: "d-flex gap-2")[
                    Button("button",
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "nf-list-add",
                        OnClick: () =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))[
                        I(Class: "bi bi-plus-lg me-1"), "Add row"],
                    Button("submit", Class: "btn btn-primary btn-sm", Id: "nf-list-submit")[
                        I(Class: "bi bi-check2-circle me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0", Id: "nf-list-result")[_submission]];
    }
}

// Collection binding via indexer — the for-loop variant. Useful when the row index matters
// (row numbers, reorder controls) or when items are records that get replaced rather than
// mutated. The `var i = idx;` per-iteration capture dodges the classic C# closure trap.
public sealed class NestedListIndexerDemo : Component
{
    private readonly InvoiceModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedListIndexerDemo() => _model.Skus.Add(new SkuRow { Code = "WIDGET-1", Price = 9.99m });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render()
    {
        var rows = new List<Child>();
        for (var idx = 0; idx < _model.Skus.Count; idx++)
        {
            var i = idx; // Per-iteration capture — without this every lambda closes over Skus.Count.
            rows.Add(Tr(Key: _model.Skus[i].Id)[
                Td(Class: "text-secondary small")[$"#{i + 1}"],
                Td()[
                    Input(() => _model.Skus[i].Code, Class: "form-control form-control-sm"),
                    ValidationMessage(() => _model.Skus[i].Code, FieldError)
                ],
                Td(Style: "width: 7rem;")[
                    Input(() => _model.Skus[i].Price, Class: "form-control form-control-sm"),
                    ValidationMessage(() => _model.Skus[i].Price, FieldError)
                ],
                Td(Style: "width: 5rem;")[
                    Button("button",
                        Class: "btn btn-outline-secondary btn-sm me-1",
                        Disabled: i == 0,
                        OnClick: () => (_model.Skus[i - 1], _model.Skus[i]) = (_model.Skus[i], _model.Skus[i - 1]))[
                        I(Class: "bi bi-arrow-up")],
                    Button("button",
                        Class: "btn btn-outline-danger btn-sm",
                        OnClick: () => _model.Skus.RemoveAt(i))[I(Class: "bi bi-x-lg")]
                ]
            ]);
        }

        return [
            Form<InvoiceModel>(
                _model,
                m => _submission =
                    $"Invoice with {m.Skus.Count} sku line(s) at total {m.Skus.Sum(s => s.Price):F2}",
                Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Table(Class: "table table-sm align-middle mb-0")[
                    Thead()[Tr()[Th(Style: "width: 3rem;")["#"], Th()["SKU"], Th()["Price"], Th()]],
                    Tbody()[rows]
                ],
                Div(Class: "d-flex gap-2")[
                    Button("button",
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "nf-idx-add",
                        OnClick: () => _model.Skus.Add(new SkuRow { Code = $"WIDGET-{_seq++}", Price = 1.00m }))[
                        I(Class: "bi bi-plus-lg me-1"), "Add row"],
                    Button("submit", Class: "btn btn-primary btn-sm", Id: "nf-idx-submit")[
                        I(Class: "bi bi-check2-circle me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? Fragment()
                : Div(Class: "alert alert-success small mt-3 mb-0", Id: "nf-idx-result")[_submission]];
    }
}

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
        Fragment()[msgs.Select((m, i) => (Child)Div(Key: i, Class: "text-danger small mt-1")[m])];

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

        return [
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
                : Div(Class: "alert alert-success small mt-3 mb-0", Id: "nf-fv-result")[_submission]];
    }
}

// Models for the demos above.

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

public sealed class CartModel
{
    public List<LineItem> Items { get; set; } = new();
}

public sealed class LineItem
{
    // Stable per-instance key for keyed row diffing (not bound to any input, no validation attrs).
    public Guid Id { get; } = Guid.NewGuid();

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(80)]
    public string Description { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;
}

public sealed class InvoiceModel
{
    public List<SkuRow> Skus { get; set; } = new();
}

public sealed class SkuRow
{
    // Stable per-instance key for keyed row diffing — survives the up/down reorder.
    public Guid Id { get; } = Guid.NewGuid();

    [Required(ErrorMessage = "SKU is required.")]
    [RegularExpression("^[A-Z0-9-]{3,12}$", ErrorMessage = "Use uppercase letters, digits, and dashes (3-12 chars).")]
    public string Code { get; set; } = "";

    [Range(0.01, 99999.99, ErrorMessage = "Price must be greater than 0.")]
    public decimal Price { get; set; } = 0.01m;
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
