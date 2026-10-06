namespace Rask.Site.Features;

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
                Td.Class("text-ui-muted text-sm")[$"#{i + 1}"],
                Td[
                    Ui.Input.Bind(() => _model.Skus[i].Code).AccessibleLabel("SKU").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.Skus[i].Code)
                ],
                Td.Style("width: 7rem;")[
                    Ui.Input.Bind(() => _model.Skus[i].Price).AccessibleLabel("Price").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.Skus[i].Price)
                ],
                Td.Style("width: 5rem;")[
                    Ui.Button.Icon(Ui.IconName.ArrowUp)
                        .AriaLabel("Move up")
                        .Class("me-1")
                        .Disabled(i == 0)
                        .OnClick(() => (_model.Skus[i - 1], _model.Skus[i]) = (_model.Skus[i], _model.Skus[i - 1])),
                    Ui.Button.Red.Icon(Ui.IconName.XMark)
                        .AriaLabel("Remove SKU")
                        .OnClick(() => _model.Skus.RemoveAt(i))
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnSubmit(m => _submission =
                    $"Invoice with {m.Skus.Count} sku line(s) at total {m.Skus.Sum(s => s.Price):F2}").Class("flex flex-col gap-3")[
                Ui.Table.Class("align-middle mb-0")[
                    Thead[Tr[Th.Style("width: 3rem;")["#"], Th["SKU"], Th["Price"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Ui.Button.Icon(Ui.IconName.Plus)
                        .Id("nf-idx-add")
                        .OnClick(() => _model.Skus.Add(new SkuRow { Code = $"WIDGET-{_seq++}", Price = 1.00m }))["Add row"],
                    Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit.Id("nf-idx-submit")["Submit"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Alert.Success.Soft.Class("text-sm mt-3 mb-0").Id("nf-idx-result")[_submission]
        ];
    }
}
