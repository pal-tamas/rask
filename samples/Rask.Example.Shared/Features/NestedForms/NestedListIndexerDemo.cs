using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// Collection binding via indexer — the for-loop variant. Useful when the row index matters
// (row numbers, reorder controls) or when items are records that get replaced rather than
// mutated. The `var i = idx;` per-iteration capture dodges the classic C# closure trap.
public sealed partial class NestedListIndexerDemo : Component
{
    private readonly InvoiceModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedListIndexerDemo() => _model.Skus.Add(new SkuRow { Code = "WIDGET-1", Price = 9.99m });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
        for (var idx = 0; idx < _model.Skus.Count; idx++)
        {
            var i = idx; // Per-iteration capture — without this every lambda closes over Skus.Count.
            rows.Add(Tr.Key(_model.Skus[i].Id)[
                Td.Class("text-secondary small")[$"#{i + 1}"],
                Td[
                    Input(() => _model.Skus[i].Code).Class("form-control form-control-sm"),
                    ValidationMessage(() => _model.Skus[i].Code, FieldError)
                ],
                Td.Style("width: 7rem;")[
                    Input(() => _model.Skus[i].Price).Class("form-control form-control-sm"),
                    ValidationMessage(() => _model.Skus[i].Price, FieldError)
                ],
                Td.Style("width: 5rem;")[
                    BsButton
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Class("me-1")
                        .Disabled(i == 0)
                        .OnClick(() => (_model.Skus[i - 1], _model.Skus[i]) = (_model.Skus[i], _model.Skus[i - 1]))[
                        BsIcon.Name(BsIconName.ArrowUp)],
                    BsButton
                        .Color(BsColor.Danger)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .OnClick(() => _model.Skus.RemoveAt(i))[BsIcon.Name(BsIconName.XLg)]
                ]
            ]);
        }

        return
        [
            Form<InvoiceModel>(
                _model,
                m => _submission =
                    $"Invoice with {m.Skus.Count} sku line(s) at total {m.Skus.Sum(s => s.Price):F2}",
                Class: "vstack gap-3")[
                DataAnnotationsValidator,
                Table.Class("table table-sm align-middle mb-0")[
                    Thead[Tr[Th.Style("width: 3rem;")["#"], Th["SKU"], Th["Price"], Th]],
                    Tbody[rows]
                ],
                BsStack.Gap(2)[
                    BsButton
                        .Color(BsColor.Secondary)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Id("nf-idx-add")
                        .OnClick(() => _model.Skus.Add(new SkuRow { Code = $"WIDGET-{_seq++}", Price = 1.00m }))[
                        BsIcon.Name(BsIconName.PlusLg).Class("me-1"), "Add row"],
                    BsButton.Type("submit").Color(BsColor.Primary).Size(BsSize.Sm).Id("nf-idx-submit")[
                        BsIcon.Name(BsIconName.Check2Circle).Class("me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0").Id("nf-idx-result")[_submission]
        ];
    }
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
