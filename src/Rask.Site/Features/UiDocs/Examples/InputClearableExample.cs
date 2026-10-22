namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputClearableExample : Component
{
    private string _search = "Jack Skellington";

    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.Input.Value(_search).Placeholder("Search orders").Clearable()
                .OnInput(text => { _search = text; }).OnChange(text => { _search = text; }),
            Ui.Input.Value("password").Type(InputType.Password).Viewable(),
            Ui.Input.Value("FLUX-1234-5678-ABCD-EFGH").Icon(Ui.IconName.Key).ReadOnly().Copyable(),
            Ui.Text.Data("testid", "ui-input-clearable-state")[$"Searching for \"{_search}\"."]
        ];
}
