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
                    UiInput.Bind(() => captured.Description).AccessibleLabel("Description").ShowValidation(false),
                    ValidationMessage.Template(FieldError).For(() => captured.Description)
                ],
                Td.Style("width: 6rem;")[
                    UiInput.Bind(() => captured.Quantity).AccessibleLabel("Quantity").ShowValidation(false),
                    ValidationMessage.Template(FieldError).For(() => captured.Quantity)
                ],
                Td.Style("width: 3rem;")[
                    UiButton
                        .AccessibleLabel("Remove item")
                        .Square(true)
                        .Tone(UiTone.Error)
                        .Variant(UiVariant.Outline)
                        .OnClick(() => _model.Items.Remove(captured))[UiIcon.Name(UiIconName.Close)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnValidSubmit(m => _submission = $"Submitted {m.Items.Count} line item(s).").Class("flex flex-col gap-3")[
                UiTable.Class("align-middle mb-0")[
                    Thead[Tr[Th["Description"], Th["Quantity"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    UiButton.Variant(UiVariant.Outline)
                        .Id("nf-list-add")
                        .OnClick(() =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))[UiIcon.Name(UiIconName.Plus), "Add row"],
                    UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit).Id("nf-list-submit")[UiIcon.Name(UiIconName.CheckCircle), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0").Id("nf-list-result")[_submission]
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
