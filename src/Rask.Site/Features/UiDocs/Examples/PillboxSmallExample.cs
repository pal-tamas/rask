namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxSmallExample : Component
{
    private static readonly string[] Tags = ["Design", "Development", "Marketing", "Sales", "Support", "Engineering", "Product", "Operations"];

    private List<string> _tags = [];

    protected override Component? Render() =>
        Ui.Pillbox.Values(_tags).OnChange(picked => { _tags = [.. picked]; }).Sm.Placeholder("Choose tags...").Class("max-w-80")[
            Tags.Select(tag => Ui.PillboxOption.Key(tag)[tag])
        ];
}
