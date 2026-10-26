namespace Rask;

// In a Ui.Filters: bound to the address, live, and said by the option it holds.
public sealed partial class UiSelect<T> : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.Select");
        var value = cell.Value;
        var active = UiFilterAddress.Holds(value);

        return new UiFilterEntry(Label ?? cell.Key, active ? Picked(value!) : null, active, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set)
    {
        var cell = UiFilterCell<T>.Over(Bind, "Ui.Select").In(set);
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        BuilderRuntime.MarkChanged(this);
    }
}
