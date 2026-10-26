namespace Rask;

// In a Ui.Filters: bound to the address, live, and said by its label alone while it is on.
public sealed partial class UiSwitch : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<bool>.Over(Bind, "Ui.Switch");
        return new UiFilterEntry(Label ?? cell.Key, null, cell.Value, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set)
    {
        var cell = UiFilterCell<bool>.Over(Bind, "Ui.Switch").In(set);
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        BuilderRuntime.MarkChanged(this);
    }
}
