using FluentValidation;

namespace Rask.Site.Features;

// FluentValidation with SetValidator + RuleForEach — one root validator covers the whole
// graph; Rask routes dotted property paths back to the runtime sub-instance.
public sealed partial class NestedFluentValidationDemo : Component
{
    private readonly NestedOrderModel _model = new();
    private int _seq = 2;
    private string? _submission;

    public NestedFluentValidationDemo() => _model.Lines.Add(new NestedOrderLine { Sku = "BOX-1", Quantity = 3 });

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render()
    {
        var rows = new List<Component>();
        foreach (var line in _model.Lines)
        {
            var captured = line;
            rows.Add(Ui.TableRow.Key(captured.Id)[
                Ui.TableCell[
                    Ui.Input.Bind(() => captured.Sku).Label("SKU").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Sku)
                ],
                Ui.TableCell.Style("width: 6rem;")[
                    Ui.Input.Bind(() => captured.Quantity).Label("Quantity").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Quantity)
                ],
                Ui.TableCell.Style("width: 3rem;")[
                    Ui.Button.Red.Icon(Ui.IconName.XMark)
                        .AriaLabel("Remove line")
                        .OnClick(() => _model.Lines.Remove(captured))
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnSubmit(m => _submission = $"Order routed: {m.CustomerName} → {m.Address.Street}, {m.Lines.Count} line(s)").Class("flex flex-col gap-3")[
                Div[
                    Ui.Input.Bind(() => _model.CustomerName).Label("Customer").Id("nf-fv-name").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => _model.CustomerName)
                ],
                Fieldset.Class("border rounded p-3")[
                    Legend.Class("text-base font-semibold")["Address"],
                    Div.Class("flex flex-col gap-2")[
                        Div[
                            Ui.Input.Bind(() => _model.Address.Street).Label("Street").ShowValidation(false),
                            Validation.Message.Template(FieldError).For(() => _model.Address.Street)
                        ],
                        Div[
                            Ui.Input.Bind(() => _model.Address.City).Label("City").ShowValidation(false),
                            Validation.Message.Template(FieldError).For(() => _model.Address.City)
                        ]
                    ]
                ],
                Ui.Table.ContainerClass("mt-2")[
                    Ui.TableColumns[Ui.TableColumn["SKU"], Ui.TableColumn["Qty"], Ui.TableColumn],
                    Ui.TableRows[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Ui.Button.Icon(Ui.IconName.Plus)
                        .Id("nf-fv-add")
                        .OnClick(() => _model.Lines.Add(new NestedOrderLine { Sku = $"BOX-{_seq++}", Quantity = 1 }))["Add line"],
                    Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit.Id("nf-fv-submit")["Place"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Callout.Success.Class("mt-3").Id("nf-fv-result").Role("status").Text(_submission)
        ];
    }
}
