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
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
        for (var idx = 0; idx < _model.Skus.Count; idx++)
        {
            var i = idx; // Per-iteration capture — without this every lambda closes over Skus.Count.
            rows.Add(Tr.Key(_model.Skus[i].Id)[
                Td.Class("text-slate-500 dark:text-slate-400 text-sm")[$"#{i + 1}"],
                Td[
                    Input.Bind(() => _model.Skus[i].Code).Class(Tw.Input),
                    ValidationMessage.Template(FieldError).For(() => _model.Skus[i].Code)
                ],
                Td.Style("width: 7rem;")[
                    Input.Bind(() => _model.Skus[i].Price).Class(Tw.Input),
                    ValidationMessage.Template(FieldError).For(() => _model.Skus[i].Price)
                ],
                Td.Style("width: 5rem;")[
                    Button.Class($"{Tw.BtnOutlineSecondary} me-1").Type("button")
                        .Disabled(i == 0)
                        .OnClick(() => (_model.Skus[i - 1], _model.Skus[i]) = (_model.Skus[i], _model.Skus[i - 1]))[
                        Icon.Name(IconName.ArrowUp)],
                    Button.Type("button").Class(Tw.BtnOutlineDanger)
                        .OnClick(() => _model.Skus.RemoveAt(i))[Icon.Name(IconName.XLg)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnValidSubmit(m => _submission =
                    $"Invoice with {m.Skus.Count} sku line(s) at total {m.Skus.Sum(s => s.Price):F2}").Class("flex flex-col gap-3")[
                DataAnnotationsValidator,
                Table.Class($"{Tw.Table} text-sm align-middle mb-0")[
                    Thead[Tr[Th.Style("width: 3rem;")["#"], Th["SKU"], Th["Price"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Button.Type("button").Class(Tw.BtnOutlineSecondary)
                        .Id("nf-idx-add")
                        .OnClick(() => _model.Skus.Add(new SkuRow { Code = $"WIDGET-{_seq++}", Price = 1.00m }))[
                        Icon.Name(IconName.PlusLg).Class("me-1"), "Add row"],
                    Button.Class(Tw.BtnPrimary).Type("submit").Id("nf-idx-submit")[
                        Icon.Name(IconName.Check2Circle).Class("me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : Div.Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0").Id("nf-idx-result")[_submission]
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
