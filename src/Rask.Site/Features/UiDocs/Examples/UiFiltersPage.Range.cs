using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

public sealed partial class UiFiltersPage
{
    private static readonly (string Plate, int Weight)[] Trucks =
        [("ABC-123", 3500), ("DEF-456", 5200), ("GHK-789", 7500), ("LMN-012", 12000)];

    [QueryParam("weightFrom")] public int? WeightFrom { get; set; }

    [QueryParam("weightTo")] public int? WeightTo { get; set; }

    private Component RangeExample()
    {
        var filters = Ui.Filters[Ui.Filter.Range(() => WeightFrom, () => WeightTo).Label("Weight")];
        var shown = Trucks.Where(truck => truck.Weight >= (WeightFrom ?? int.MinValue) && truck.Weight <= (WeightTo ?? int.MaxValue));

        return Div.Class("space-y-4")[
            filters,
            filters.Badges,
            Ui.Text[string.Join(", ", shown.Select(truck => truck.Plate)) is { Length: > 0 } plates ? plates : "No truck."]
        ];
    }
}
