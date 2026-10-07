namespace Rask;

/// <summary>The box of a checkbox, for a card whose content you lay out yourself.</summary>
/// <remarks>
/// Flux UI's <c>flux:checkbox.indicator</c>: <c>Ui.Checkbox.Value("news")[Ui.CheckboxIndicator, Div[…]]</c>
/// inside a group drawn as cards. It follows the checkbox it is written in — ticked, indeterminate, disabled.
/// </remarks>
public sealed partial class UiCheckboxIndicator : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(UiOptionLook.CheckboxIndicator, Class)).Data("ui-checkbox-indicator", "")[
            Ui.Icon.Name(Ui.IconName.Check).Micro.Class(UiOptionLook.Tick),
            Ui.Icon.Name(Ui.IconName.Minus).Micro.Class(UiOptionLook.Dash)
        ];
}
