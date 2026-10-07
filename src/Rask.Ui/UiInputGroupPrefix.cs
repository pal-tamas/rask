namespace Rask;

/// <summary>
/// Text fused to the front of an input in a <see cref="UiInputGroup" />: <c>Ui.InputGroupPrefix["https://"]</c>.
/// </summary>
public sealed partial class UiInputGroupPrefix : Component
{
    /// <summary>Match the input beside it. 40px unless this says smaller.</summary>
    public Ui.InputSize? Size { get; set; }

    /// <summary>Classes for the prefix.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(UiInputGroupAffix.Look, "border-e-0 rounded-s-lg", UiInputGroupAffix.Size(Size), Class))
            .Data("ui-input-group-prefix", "")[Children ?? []];
}
