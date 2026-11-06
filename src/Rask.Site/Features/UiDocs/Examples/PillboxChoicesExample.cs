namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxChoicesExample : Component
{
    // Rows of another table, in the order the query happened to bring them.
    private static readonly Ui.Choice<int>[] Vehicles =
    [
        new(3, "XYZ-789", "Scania R450"), new(1, "ÁBC-123", "Volvo FH16"), new(2, "ABD-456", "MAN TGX"),
        new(4, "CSE-321", "DAF XF"),
    ];

    private readonly Assignment _assignment = new();

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Field[
                Ui.Label["Vehicles"],
                Ui.Pillbox.Bind(() => _assignment.VehicleIds).Id("ui-pillbox-choices").Live().Choices(Vehicles)
            ],
            Ui.Text.Data("testid", "ui-pillbox-choices-state")[$"Vehicles: {string.Join(", ", _assignment.VehicleIds)}."]
        ];

    public sealed class Assignment
    {
        public ICollection<int> VehicleIds { get; set; } = [];
    }
}
