namespace Rask;

// In a Ui.Filters: bound to the address, live, and said as the picker's own button says the choice.
public abstract partial class UiDatePickerControl<T> : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.DatePicker");
        var value = cell.Value;
        var active = UiFilterAddress.Holds(value);

        return new UiFilterEntry(Label ?? cell.Key, active ? Text(value, Options()) : null, active, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.DatePicker");
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        BuilderRuntime.MarkChanged(this);
    }
}
