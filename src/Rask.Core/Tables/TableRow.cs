namespace Rask.Core.Tables;

// Wrapper for one row the host passed in `Rows`: the value plus its stable identity, its position on
// the current page, the selection flag, and a ToggleSelected action that proposes the next selection
// set via OnSelect (a no-op when no OnSelect/OnSelectAsync callback was supplied).
//   • Key      — KeySelector(value) when supplied, else the row reference itself.
//   • RowIndex — index within the current `Rows` window (the host-supplied page), 0-based.
public sealed record TableRow<T>(
    T Value,
    object Key,
    int RowIndex,
    bool IsSelected,
    Action ToggleSelected);
