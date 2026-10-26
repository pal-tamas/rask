using System.Globalization;

namespace Rask;

// In a Ui.Filters: bound to the address, written at the pause in typing unless the call site says when, and said
// as it was typed, trimmed.
public sealed partial class UiInput<T> : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.Input");
        var value = cell.Value;
        var active = UiFilterAddress.Holds(value);

        return new UiFilterEntry(Label ?? cell.Key, active ? Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() : null, active, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set)
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.Input").In(set);
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        if (Blur is null && Debounce is null)
        {
            Live ??= true;
        }

        BuilderRuntime.MarkChanged(this);
    }
}
