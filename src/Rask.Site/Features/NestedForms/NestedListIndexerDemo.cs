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
            rows.Add(Ui.TableRow.Key(_model.Skus[i].Id)[
                Ui.TableCell.Class("text-ui-muted text-sm")[$"#{i + 1}"],
                Ui.TableCell[
                    Ui.Input.Bind(() => _model.Skus[i].Code).AccessibleLabel("SKU").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.Skus[i].Code)
                ],
                Ui.TableCell.Style("width: 7rem;")[
                    Ui.Input.Bind(() => _model.Skus[i].Price).AccessibleLabel("Price").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.Skus[i].Price)
                ],
                Ui.TableCell.Style("width: 5rem;")[
                    Ui.Button
                        .AccessibleLabel("Move up")
                        .Square()
                        .Outline
                        .Class("me-1")
                        .Disabled(i == 0)
                        .OnClick(() => (_model.Skus[i - 1], _model.Skus[i]) = (_model.Skus[i], _model.Skus[i - 1]))[Ui.Icon.Name(Ui.IconName.ArrowUp)],
                    Ui.Button
                        .AccessibleLabel("Remove SKU")
                        .Square()
                        .Error
                        .Outline
                        .OnClick(() => _model.Skus.RemoveAt(i))[Ui.Icon.Name(Ui.IconName.Close)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnSubmit(m => _submission =
                    $"Invoice with {m.Skus.Count} sku line(s) at total {m.Skus.Sum(s => s.Price):F2}").Class("flex flex-col gap-3")[
                Ui.Table[
                    Ui.TableColumns[Ui.TableColumn.Style("width: 3rem;")["#"], Ui.TableColumn["SKU"], Ui.TableColumn["Price"], Ui.TableColumn],
                    Ui.TableRows[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Ui.Button.Outline
                        .Id("nf-idx-add")
                        .OnClick(() => _model.Skus.Add(new SkuRow { Code = $"WIDGET-{_seq++}", Price = 1.00m }))[Ui.Icon.Name(Ui.IconName.Plus), "Add row"],
                    Ui.Button.Primary.Submit.Id("nf-idx-submit")[Ui.Icon.Name(Ui.IconName.CheckCircle), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Alert.Success.Soft.Class("text-sm mt-3 mb-0").Id("nf-idx-result")[_submission]
        ];
    }
}
