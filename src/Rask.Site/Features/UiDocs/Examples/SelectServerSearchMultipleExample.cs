namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectServerSearchMultipleExample : Component
{
    private ICollection<int> _partnerIds = [];

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Values(_partnerIds).OnChange(ids => { _partnerIds = ids; }).Listbox.Multiple().Placeholder("Choose partners...")
                .Id("ui-select-server-many").Search(async typed =>
                    PartnerStore.Named(typed).Take(20).Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name])),
            Ui.Text.Data("testid", "ui-select-server-many-state")[$"Partners: {string.Join(", ", _partnerIds)}."]
        ];
}
