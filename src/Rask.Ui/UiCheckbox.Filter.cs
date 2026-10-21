namespace Rask;

// In a Ui.Filters: bound to the address, live, and said by its label alone while it is ticked.
public sealed partial class UiCheckbox : IUiFilterField
{
    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter()
    {
        var cell = UiFilterCell<bool?>.Over(Bind, "Ui.Checkbox");
        return new UiFilterEntry(Label ?? cell.Key, null, cell.Value == true, cell.Key);
    }

    /// <inheritdoc />
    void IUiFilterField.Take()
    {
        var cell = UiFilterCell<bool?>.Over(Bind, "Ui.Checkbox");
        if (cell.IsNot(Bind))
        {
            Id ??= UiFieldId.Derive(null, Bind, Label);
            Bind = cell.Bind;
        }

        Live ??= true;

        BuilderRuntime.MarkChanged(this);
    }
}
