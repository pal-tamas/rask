namespace Rask.Ui;

/// <summary>
/// Rows of information, each a small grid.
/// </summary>
public sealed partial class UiList : Ul
{


    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose("list rounded-box bg-base-100", Class);
}
