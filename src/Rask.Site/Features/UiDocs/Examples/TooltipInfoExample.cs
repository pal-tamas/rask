namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipInfoExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex items-center gap-2 text-sm font-medium")[
            "Tax identification number",
            Ui.Tooltip.Toggleable()[
                Ui.Button.Sm.Ghost.Icon(Ui.IconName.InformationCircle),
                Ui.TooltipContent.Class("max-w-[20rem] space-y-2")[
                    P["For US businesses, enter your 9-digit Employer Identification Number (EIN) without hyphens."],
                    P["For European companies, enter your VAT number including the country prefix (e.g., DE123456789)."]
                ]
            ]
        ];
}
