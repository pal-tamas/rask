using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

public sealed partial class UiFiltersPage
{
    private static readonly string[] Labels = ["Fragile", "Express", "Gift"];

    [QueryParam("labels")] public string? Tagged { get; set; }

    private Component FilterExample()
    {
        List<string> tagged = [.. (Tagged ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)];

        var filters = Ui.Filters[
            Ui.Filter.Label("Labels").Shown(tagged.Count == 0 ? null : string.Join(", ", tagged)).OnRemove(() => Go.With("labels", null))[
                Ui.Select.Values((ICollection<string>)tagged).Label("Labels").Listbox.Multiple().Placeholder("Any label")
                    .OnChange((ICollection<string> picked) => Go.With("labels", picked.Count == 0 ? null : string.Join(',', picked)))[
                    Labels.Select(label => Ui.SelectOption.Key(label).Value(label)[label])
                ]
            ]
        ];

        return Div.Class("space-y-4")[
            filters,
            filters.Badges,
            Ui.Text[filters.Active == 0 ? "Every order." : $"Orders labelled {string.Join(" or ", tagged)}."]
        ];
    }
}
