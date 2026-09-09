namespace Rask.Ui;

/// <summary>The key-and-value list a detail sheet is made of.</summary>
public sealed partial class UiDetailList : Div
{

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        "divide-y divide-ui-line";
}
