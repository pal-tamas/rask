namespace Rask.Example.Shared.Features;

// BsCheckboxGroup<TItem> — selecting many values into a collection, here in controlled mode (Value + OnChange).
// The parent owns the selection; OnChange (auto-wrapped) re-renders this demo so the summary stays live.
public sealed partial class MultiSelectCheckboxDemo : Component
{
    private static readonly string[] AllInterests = ["Web", "Mobile", "AI", "Games"];

    private ICollection<string> _interests = [];

    protected override Component? Render() =>
        Div.Class("vstack gap-3")[
            Div[
                Label.Class("form-label fw-semibold d-block")["Interests"],
                BsCheckboxGroup
                    .Value(_interests.ToList())
                    .Options(AllInterests)
                    .OnChange(next => _interests = next)
                    .ItemClass("form-check-inline")
            ],
            P.Class("small text-secondary mb-0").Id("ms-checkbox-summary")[
                "Selected: " + (_interests.Count == 0 ? "none" : string.Join(", ", _interests))
            ]
        ];
}
