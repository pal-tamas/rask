namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectCustomContentExample : Component
{
    private string? _color;

    protected override Component? Render() =>
        Ui.Select.Value(_color).OnChange(value => { _color = value; }).Listbox.Placeholder("Select color...").Class("max-w-sm")[
            Ui.SelectOption.Value("red")[
                Div.Class("flex items-center gap-2")[Div.Class("size-4 rounded-full bg-red-500"), "Red"]
            ],
            Ui.SelectOption.Value("orange")[
                Div.Class("flex items-center gap-2")[Div.Class("size-4 rounded-full bg-orange-500"), "Orange"]
            ],
            Ui.SelectOption.Value("amber")[
                Div.Class("flex items-center gap-2")[Div.Class("size-4 rounded-full bg-amber-500"), "Amber"]
            ],
            Ui.SelectOption.Value("yellow")[
                Div.Class("flex items-center gap-2")[Div.Class("size-4 rounded-full bg-yellow-500"), "Yellow"]
            ]
        ];
}
