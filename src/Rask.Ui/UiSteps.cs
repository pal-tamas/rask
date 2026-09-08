namespace Rask.Ui;

/// <summary>
/// Progress through a sequence of named steps.
/// </summary>
public sealed partial class UiSteps : Component
{
    /// <summary>Stacks the steps vertically, which is what a phone has room for.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "steps",
            Vertical == true ? "steps-vertical" : "steps-horizontal",
            Class))[Children ?? []];
}
