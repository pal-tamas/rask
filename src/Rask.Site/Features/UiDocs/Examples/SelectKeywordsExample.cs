namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectKeywordsExample : Component
{
    private string? _category;

    protected override Component? Render() =>
        Ui.Select.Value(_category).OnChange(value => { _category = value; }).Listbox.Searchable().Placeholder("Choose category...").Class("max-w-sm")[
            Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
            Ui.SelectOption.Value("vegetables").Keywords("carrot broccoli spinach")["Vegetables"],
            Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
        ];
}
