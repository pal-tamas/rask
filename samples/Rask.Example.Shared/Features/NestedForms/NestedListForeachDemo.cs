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
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
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
                    BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, OnClick: () => _model.Items.Remove(captured))[BsIcon(Name: BsIconName.XLg)]
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
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "nf-list-add", OnClick: () =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))[
                        BsIcon(Name: BsIconName.PlusLg, Class: "me-1"), "Add row"],
                    BsButton(Type: "submit", Color: BsColor.Primary, Size: BsSize.Sm, Id: "nf-list-submit")[
                        BsIcon(Name: BsIconName.Check2Circle, Class: "me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? null
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
