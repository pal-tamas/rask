namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectAvatarsExample : Component
{
    private string? _person;

    protected override Component? Render() =>
        Ui.Select.Value(_person).OnChange(value => { _person = value; }).Listbox.Placeholder("Search people...").Class("max-w-sm")[
            Ui.SelectOption.Value("maralind").Label("Mara Lind").Avatar("/img/avatars/amber.svg"),
            Ui.SelectOption.Value("theobrandt").Label("Theo Brandt").Avatar("/img/avatars/sky.svg"),
            Ui.SelectOption.Value("rosavidal").Label("Rosa Vidal").Avatar("/img/avatars/rose.svg")
        ];
}
