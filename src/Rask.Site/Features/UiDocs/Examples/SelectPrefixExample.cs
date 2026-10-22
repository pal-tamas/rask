namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectPrefixExample : Component
{
    private string Comparison { get; set; } = "last-month";

    protected override Component? Render() =>
        Ui.Select.Bind(() => Comparison).Listbox.Prefix("Compare to").Class("max-w-sm")[
            Ui.SelectOption.Value("previous-period")["Previous period"],
            Ui.SelectOption.Value("last-month")["Last month"],
            Ui.SelectOption.Value("last-quarter")["Last quarter"],
            Ui.SelectOption.Value("last-year")["Last year"]
        ];
}
