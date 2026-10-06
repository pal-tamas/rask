namespace Rask;

/// <summary>Help text for a form control or a fieldset: a format, a constraint, why it is asked for.</summary>
/// <remarks>
/// Flux UI's description. Before the control it sits under the label; after it, under the control. Inside a
/// <see cref="UiField" /> the control names it with <c>aria-describedby</c>.
/// </remarks>
public sealed partial class UiDescription : Component
{
    /// <summary>Its id. Inside a field it is derived from the control's.</summary>
    public string? Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Id(Id ?? Context.Get<UiFieldScope>()?.DescriptionId)
            .Class(UiClass.Compose("text-sm text-zinc-500 dark:text-white/60", Class))
            .Data("ui-description", "")[Children ?? []];
}
