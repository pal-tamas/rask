namespace Rask;

/// <summary>The heading of a <see cref="UiFieldset" />: <c>Ui.Legend["Shipping address"]</c>.</summary>
/// <remarks>Flux UI's legend, as a real <c>&lt;legend&gt;</c>, which is what names the fieldset.</remarks>
public sealed partial class UiLegend : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Legend.Class("[:where(&)]:mb-4 [:where(&)]:w-full [:where(&)]:p-0 [:where(&)]:text-base font-medium [:where(&)]:text-zinc-800 dark:text-white", Class)
            .Data("ui-legend", "")[Children ?? []];
}
