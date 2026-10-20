using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverCategoriesExample : Component
{
    private static readonly (string Value, string Label)[] Categories =
    [
        ("fantasy", "Fantasy"), ("science-fiction", "Science fiction"), ("horror", "Horror"), ("mystery", "Mystery"),
        ("romance", "Romance"), ("autobiography", "Autobiography"), ("thriller", "Thriller"), ("poetry", "Poetry"),
        ("children", "Children"),
    ];

    private List<string> _picked = ["fantasy", "science-fiction", "thriller"];

    protected override Component? Render() =>
        Ui.Dropdown[
            Ui.Button.Icon(Ui.IconName.Tag).IconTrailing(
                Ui.Badge.Sm.Class("-mr-1 tabular-nums").Data("testid", "ui-popover-count")[
                    _picked.Count.ToString(CultureInfo.InvariantCulture)
                ])["Categories"],
            Ui.Popover.Class("max-w-[18rem] flex flex-col gap-4")[
                Ui.CheckboxGroup.Values(_picked).Id("popover-categories").Variant(Ui.CheckboxGroupVariant.Pills)
                    .OnChange(v => { _picked = [.. v]; })[
                    Categories.Select(category => Ui.Checkbox.Key(category.Value).Value(category.Value).Label(category.Label))
                ],
                Ui.Separator.Subtle,
                Ui.Button.Subtle.Sm.Class("justify-start -m-2 px-2!").OnClick(() => { _picked = []; })["Clear all"]
            ]
        ];
}
