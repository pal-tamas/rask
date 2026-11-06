using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectChoicesExample : Component
{
    // Rows of another table, in the order the query happened to bring them.
    private static readonly Ui.Choice<int>[] Companies =
    [
        new(4, "Zenith Express"), new(1, "Ábel Transport", "Budapest"), new(2, "Bergmann Logistik", "Hamburg"),
        new(3, "Alpine Freight", "Innsbruck"), new(5, "Danube Express"),
    ];

    private readonly Delivery _delivery = new();

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Field[
                Ui.Label["Company"],
                // int? and not required: the cross clears it.
                Ui.Select.Bind(() => _delivery.CompanyId).Id("ui-select-choices").Live().Choices(Companies)
            ],
            Ui.Field[
                Ui.Label["Driver"],
                // A list too long to send whole: the store answers what is typed.
                Ui.Select.Bind(() => _delivery.DriverId).Id("ui-select-choices-search").Live()
                    .Choices(Drivers, held: new Ui.Choice<int>(4, "Bergmann Logistik"))
            ],
            Ui.Text.Data("testid", "ui-select-choices-state")[
                $"Company: {_delivery.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "none"}. Driver: {_delivery.DriverId?.ToString(CultureInfo.InvariantCulture) ?? "none"}."
            ]
        ];

    // The store's answer: the best eight for what was typed — a query, here over a list.
    private static Task<IReadOnlyList<Ui.Choice<int>>> Drivers(string typed) =>
        Task.FromResult<IReadOnlyList<Ui.Choice<int>>>([.. PartnerStore.Named(typed).Take(8).Select(p => new Ui.Choice<int>(p.Id, p.Name))]);

    public sealed class Delivery
    {
        public int? CompanyId { get; set; }

        public int? DriverId { get; set; } = 4;
    }
}
