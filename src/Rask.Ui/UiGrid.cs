namespace Rask.Ui;

/// <summary>The grid the overview lays its tiles on.</summary>
public sealed partial class UiGrid : Div
{

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        "grid gap-3 sm:grid-cols-2 sm:gap-4 lg:grid-cols-3";
}
