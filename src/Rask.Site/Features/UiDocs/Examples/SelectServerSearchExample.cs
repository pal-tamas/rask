namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectServerSearchExample : Component
{
    private int? _partnerId = 4;
    private int _searches;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_partnerId).OnChange(id => { _partnerId = id; }).Placeholder("Choose a partner...").Id("ui-select-server").Search(Find)[
                Ui.SelectSearch.Placeholder("Search partners..."),
                // The partner the value holds, so the select names it before anything is found.
                Ui.SelectOption.Key(4).Value(4)["Bergmann Logistik"]
            ],
            Ui.Text.Data("testid", "ui-select-server-state")[$"Partner: {(_partnerId is { } id ? $"{id}" : "none")}. Searches: {_searches}."]
        ];

    // The store's answer: the best twenty for what was typed, as Flux's backend search takes — a query, here over a list, and slow enough to read
    // the loading row.
    private async Task<IEnumerable<Component?>> Find(string typed)
    {
        _searches++;
        if (typed.Length > 0)
        {
            await Task.Delay(700.Milliseconds);
        }

        return PartnerStore.Named(typed).Take(20).Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name]);
    }
}
