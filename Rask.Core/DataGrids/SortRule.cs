namespace Rask.Core.DataGrids;

public sealed record SortRule<TRow>(string Key, bool Descending, Func<TRow, object?> Selector);
