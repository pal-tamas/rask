using System.ComponentModel.DataAnnotations;

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
            rows.Add(Tr.Key(captured.Id)[
                Td[
                    Ui.Input.Bind(() => captured.Description).AccessibleLabel("Description").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Description)
                ],
                Td.Style("width: 6rem;")[
                    Ui.Input.Bind(() => captured.Quantity).AccessibleLabel("Quantity").ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => captured.Quantity)
                ],
                Td.Style("width: 3rem;")[
                    Ui.Button
                        .AccessibleLabel("Remove item")
                        .Square(true)
                        .Tone(Ui.Tone.Error)
                        .Variant(Ui.Variant.Outline)
                        .OnClick(() => _model.Items.Remove(captured))[Ui.Icon.Name(Ui.IconName.Close)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnValidSubmit(m => _submission = $"Submitted {m.Items.Count} line item(s).").Class("flex flex-col gap-3")[
                Ui.Table.Class("align-middle mb-0")[
                    Thead[Tr[Th["Description"], Th["Quantity"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Ui.Button.Variant(Ui.Variant.Outline)
                        .Id("nf-list-add")
                        .OnClick(() =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))[Ui.Icon.Name(Ui.IconName.Plus), "Add row"],
                    Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit).Id("nf-list-submit")[Ui.Icon.Name(Ui.IconName.CheckCircle), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0").Id("nf-list-result")[_submission]
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
