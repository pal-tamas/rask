using System.Globalization;

namespace Rask;

// In a Ui.Filters: bound to the address, live, and said by the label of the radio it holds.
public sealed partial class UiRadioGroup<T> : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.RadioGroup");
        var value = cell.Value;
        var active = UiFilterAddress.Holds(value);

        return new UiFilterEntry(Label ?? cell.Key, active ? Chosen(value!) : null, active, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.RadioGroup");
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        BuilderRuntime.MarkChanged(this);
    }

    private string Chosen(T value) =>
        (Children ?? []).OfType<UiRadio>().FirstOrDefault(radio => Equals(radio.Value, value))?.Label
        ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
}
