namespace Rask.Example.Shared.Features;

// A plain OS <select multiple> bound to a model collection — the same binding BsMultiSelect offers, but
// on the native control, which is what MultiSelect(Native: true) and mobile browsers fall back to.
//
// The control reports its WHOLE selection: `select.value` is only the first selected option (the DOM has
// no multi-value `value`), so the client sends a `values` array alongside it and the binding replaces the
// collection on every change. Nothing here is StateHasChanged-driven — picking re-renders the owner, so
// the summary below updates as you select.
public sealed partial class NativeMultiSelectDemo : Component
{
    private static readonly string[] AllRegions =
        ["Europe", "North America", "South America", "Asia", "Africa", "Oceania"];

    private readonly Shipping _shipping = new();

    protected override Component? Render() =>
    [
        Form.Model(_shipping).Class("vstack gap-3")[
            Div[
                Label.Class("form-label fw-semibold").For("native-regions")["Ship to"],
                Select.Bind(() => _shipping.Regions)
                    .Multiple(true)
                    .Id("native-regions")
                    .Class("form-select")
                    .Size(6)[
                    AllRegions.Select(r => Option.Value(r).Key(r)[r])
                ],
                Div.Class("form-text")["Hold ⌘ (or Ctrl) to pick more than one."]
            ]
        ],
        BsAlert.Color(BsColor.Secondary).Class("small mt-3 mb-0").Id("native-regions-summary")[
            _shipping.Regions.Count == 0
                ? "No regions selected."
                : $"{_shipping.Regions.Count} selected: {string.Join(", ", _shipping.Regions)}"
        ]
    ];

    private sealed class Shipping
    {
        // Get-only on purpose: this is how a collection property is usually declared, and the binding
        // refills it in place rather than assigning over it.
        public List<string> Regions { get; } = [];
    }
}
