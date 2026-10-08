namespace Rask;

/// <summary>The dot of a radio, for a card whose content you lay out yourself.</summary>
/// <remarks>
/// Flux UI's <c>flux:radio.indicator</c>: <c>Ui.Radio.Value("fast")[Ui.RadioIndicator, Div[…]]</c> inside a
/// group drawn as cards. It follows the radio it is written in — chosen, disabled.
/// </remarks>
public sealed partial class UiRadioIndicator : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(UiOptionLook.RadioIndicator, Class)).Data("ui-radio-indicator", "")[
            Div.Class(UiOptionLook.Dot)
        ];
}
