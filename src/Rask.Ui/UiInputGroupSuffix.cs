namespace Rask;

/// <summary>
/// Text fused to the end of an input in a <see cref="UiInputGroup" />: <c>Ui.InputGroupSuffix[".brand.com"]</c>.
/// </summary>
public sealed partial class UiInputGroupSuffix : Component
{
    /// <summary>Match the input beside it. 40px unless this says smaller.</summary>
    public Ui.InputSize? Size { get; set; }

    /// <summary>Classes for the suffix.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(UiInputGroupAffix.Look, "border-s-0 rounded-e-lg", UiInputGroupAffix.Size(Size), Class))
            .Data("ui-input-group-suffix", "")[Children ?? []];
}
