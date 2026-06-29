using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Collection binding via foreach-capture — the canonical pattern.
public sealed class NestedListForeachDemo : Component
{
    private readonly CartModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedListForeachDemo() =>
        _model.Items.Add(new LineItem { Description = "Coffee beans (250g)", Quantity = 2 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

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

        return
        [
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
                : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0", Id: "nf-list-result")[_submission]
        ];
    }
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
