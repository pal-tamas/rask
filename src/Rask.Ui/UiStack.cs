namespace Rask.Ui;

/// <summary>
/// Elements stacked on top of one another.
/// </summary>
public sealed partial class UiStack : Div
{


    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose("stack", Class);
}
