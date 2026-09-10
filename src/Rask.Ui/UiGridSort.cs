namespace Rask.Ui;

/// <summary>
/// The sort a reader asked for, reported by <see cref="UiDataGrid{T}.OnSortChange" />.
/// </summary>
/// <param name="Field">
///     The <see cref="UiColumn{T}.Field" /> token of the column they clicked, or <see langword="null" />
///     once they have cycled the header back to no sort at all.
/// </param>
/// <param name="Descending">Which way round.</param>
public readonly record struct UiGridSort(string? Field, bool Descending);
