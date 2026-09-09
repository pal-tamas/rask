namespace Rask.Ui;

/// <summary>
/// Progress through a sequence of named steps.
/// </summary>
public sealed partial class UiSteps : Ul
{

    /// <summary>Stacks the steps vertically, which is what a phone has room for.</summary>
    public bool? Vertical { get; set; }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "steps",
            Vertical == true ? "steps-vertical" : "steps-horizontal",
            Class);
}
