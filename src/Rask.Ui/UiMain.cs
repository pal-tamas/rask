namespace Rask.Ui;

/// <summary>The console's content column, inside the frame.</summary>
public sealed partial class UiMain : Main
{

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        "bg-base-200 px-3 py-4 sm:px-5 sm:py-6";
}
