namespace Rask;

// In a Ui.Filters: bound to the address, live, and drawn and said as the picker's button says the choice.
public abstract partial class UiDatePickerControl<T> : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.DatePicker");
        var value = cell.Value;
        var active = UiFilterAddress.Holds(value);

        return new UiFilterEntry(Label ?? cell.Name, active ? Text(value, Options()) : null, active, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set)
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.DatePicker").In(set);
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(this, null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        // A filter says its choice as a button does, the day or the two days or the preset's name: typed only when
        // the page asks for it.
        Type ??= Ui.DatePickerType.Button;

        BuilderRuntime.MarkChanged(this);
    }
}
