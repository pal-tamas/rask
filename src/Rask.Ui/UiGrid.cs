namespace Rask.Ui;

/// <summary>The grid the overview lays its tiles on.</summary>
public sealed partial class UiGrid : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-3 sm:grid-cols-2 sm:gap-4 lg:grid-cols-3")[Children ?? []];
}
