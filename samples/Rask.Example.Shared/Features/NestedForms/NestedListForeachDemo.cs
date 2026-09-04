using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

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
                    Input.Bind(() => captured.Description).Class(Tw.Input),
                    ValidationMessage.Template(FieldError).For(() => captured.Description)
                ],
                Td.Style("width: 6rem;")[
                    Input.Bind(() => captured.Quantity).Class(Tw.Input),
                    ValidationMessage.Template(FieldError).For(() => captured.Quantity)
                ],
                Td.Style("width: 3rem;")[
                    Button.Type("button").Class(Tw.BtnOutlineDanger)
                        .OnClick(() => _model.Items.Remove(captured))[UiIcon.Name(UiIconName.Close)]
                ]
            ]);
        }

        return
        [
            Form.Model(_model).OnValidSubmit(m => _submission = $"Submitted {m.Items.Count} line item(s).").Class("flex flex-col gap-3")[
                Table.Class($"{Tw.Table} text-sm align-middle mb-0")[
                    Thead[Tr[Th["Description"], Th["Quantity"], Th]],
                    Tbody[rows]
                ],
                Div.Class("flex gap-2 flex-wrap items-center")[
                    Button.Type("button").Class(Tw.BtnOutlineSecondary)
                        .Id("nf-list-add")
                        .OnClick(() =>
                            _model.Items.Add(new LineItem { Description = $"New item #{_seq++}", Quantity = 1 }))[
                        UiIcon.Name(UiIconName.Plus).Class("me-1"), "Add row"],
                    Button.Class(Tw.BtnPrimary).Type("submit").Id("nf-list-submit")[
                        UiIcon.Name(UiIconName.CheckCircle).Class("me-1"), "Submit"]
                ]
            ],
            _submission is null
                ? null
                : Div.Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0").Id("nf-list-result")[_submission]
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
