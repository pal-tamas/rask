namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectDescriptionsExample : Component
{
    private string? _plan;

    protected override Component? Render() =>
        Ui.Select.Value(_plan).OnChange(value => { _plan = value; }).Listbox.Placeholder("Choose plan...").Class("max-w-sm")[
            Ui.SelectOption.Value("basic").Label("Basic").Description("For individuals getting started"),
            Ui.SelectOption.Value("pro").Label("Pro").Description("For small teams that need more power"),
            Ui.SelectOption.Value("enterprise").Label("Enterprise").Description("Advanced controls and support")
        ];
}
