namespace Rask.Dashboard.Pages;

/// <summary>What a panel says when it has nothing to show: a heading, and a line on how something gets there.</summary>
/// <remarks>Flux has no empty state of its own, so it is Flux's heading and text, centred in the space the rows would take.</remarks>
internal sealed partial class DashboardEmpty : Component
{
    public new required string Title { get; set; }

    public string? Detail { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex flex-col items-center gap-2 px-6 py-12 text-center")[
            Ui.Heading[Title],
            Detail is null ? null : Ui.Text.Class("max-w-md")[Detail]
        ];
}
