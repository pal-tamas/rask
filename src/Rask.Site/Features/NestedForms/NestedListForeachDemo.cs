namespace Rask.Site.Features;

// Collection binding via foreach-capture — the canonical pattern.
public sealed partial class NestedListForeachDemo : Component
{
    private readonly CartModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedListForeachDemo() =>
        _model.Items.Add(new LineItem { Description = "Coffee beans (250g)", Quantity = 2 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
        foreach (var item in _model.Items)
        {
            var captured = item; // foreach already captures per-iteration but make it loud.
            rows.Add(Ui.TableRow.Key(captured.Id)[
                Ui.TableCell[
                    Ui.Input.Bind(() => captured.Description).Label("Description").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Description)
                ],
                Ui.TableCell.Style("width: 6rem;")[
                    Ui.Input.Bind(() => captured.Quantity).Label("Quantity").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Quantity)
                ],
                Ui.TableCell.Style("width: 3rem;")[
                    Ui.Button.Red.Icon(Ui.IconName.XMark)
                        .AriaLabel("Remove item")
                        .OnClick(() => _model.Items.Remove(captured))
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnSubmit(m => _submission = $"Submitted {m.Items.Count} line item(s).").Class("flex flex-col gap-3")[
                Ui.Table[
                    Ui.TableColumns[Ui.TableColumn["Description"], Ui.TableColumn["Quantity"], Ui.TableColumn],
                    Ui.TableRows[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Ui.Button.Icon(Ui.IconName.Plus)
                        .Id("nf-list-add")
                        .OnClick(() =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))["Add row"],
                    Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit.Id("nf-list-submit")["Submit"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Callout.Success.Class("mt-3").Id("nf-list-result").Role("status").Text(_submission)
        ];
    }
}
