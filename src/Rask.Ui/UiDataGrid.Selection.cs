using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    private bool IsSelected(T row) =>
        Selected is { } controlled ? controlled.Contains(RowKey(row)) : _own.Contains(RowKey(row));

    private bool AllSelected(IReadOnlyList<T> rows)
    {
        return rows.Count > 0 && rows.All(IsSelected);
    }

    private Task ToggleAsync(T row, bool on)
    {
        var next = CurrentSelection();
        if (on)
        {
            next.Add(RowKey(row));
        }
        else
        {
            next.Remove(RowKey(row));
        }

        return CommitSelectionAsync(next);
    }

    private Task SetPageSelectionAsync(IReadOnlyList<T> rows, bool on)
    {
        var next = CurrentSelection();
        foreach (var row in rows)
        {
            if (on)
            {
                next.Add(RowKey(row));
            }
            else
            {
                next.Remove(RowKey(row));
            }
        }

        return CommitSelectionAsync(next);
    }

    private HashSet<TKey> CurrentSelection() => Selected is { } c ? [.. c] : [.. _own];

    private Task CommitSelectionAsync(HashSet<TKey> next)
    {
        // The uncontrolled half is only ours to hold while the parent is not holding it.
        if (Selected is null)
        {
            _own.Clear();
            foreach (var key in next)
            {
                _own.Add(key);
            }
        }

        return OnSelectionChange.Invoke(next.ToList()).AsTask();
    }

    // A row's identity for the live diff. It is the row KEY now, never the index: an index makes two
    // rows that swapped places look like two rows that changed contents, which is the reordering bug
    // the key was always meant to prevent — and before RowKey was required, an index was the fallback
    // every grid that had not named one silently got.
    private object RowIdentity(T row) => RowKey(row);
}
