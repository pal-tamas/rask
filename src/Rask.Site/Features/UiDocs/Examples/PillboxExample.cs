namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxExample : Component
{
    private static readonly string[] Tags = ["Design", "Development", "Marketing", "Sales", "Support", "Engineering", "Product", "Operations"];

    private List<string> _tags = [];

    protected override Component? Render() =>
        Div.Class("max-w-80 space-y-3")[
            Ui.Pillbox.Values(_tags).OnChange(picked => { _tags = [.. picked]; }).Placeholder("Choose tags...").Id("ui-pillbox-tags")[
                Tags.Select(tag => Ui.PillboxOption.Key(tag)[tag])
            ],
            Ui.Text.Data("testid", "ui-pillbox-state")[_tags.Count == 0 ? "Tags: none." : $"Tags: {string.Join(", ", _tags)}."]
        ];
}
