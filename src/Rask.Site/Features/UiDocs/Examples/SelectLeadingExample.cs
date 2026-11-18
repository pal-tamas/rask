using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectLeadingExample : Component
{
    // No company has the id 0: the row asks for the deliveries that have none.
    private static readonly Ui.Choice<int> NoCompany = new(0, "No company");

    private static readonly Ui.Choice<int>[] Companies =
    [
        new(4, "Zenith Express"), new(1, "Ábel Transport"), new(2, "Bergmann Logistik"), new(3, "Alpine Freight"),
    ];

    private readonly Delivery _delivery = new();

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Field[
                Ui.Label["Company"],
                Ui.Select.Bind(() => _delivery.CompanyId).Id("ui-select-leading").Live().Choices(Companies).Leading(NoCompany)
            ],
            Ui.Text.Data("testid", "ui-select-leading-state")[
                $"Company: {_delivery.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "any"}."
            ]
        ];

    public sealed class Delivery
    {
        public int? CompanyId { get; set; }
    }
}
