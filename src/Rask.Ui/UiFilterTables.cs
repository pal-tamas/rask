namespace Rask;

/// <summary>
///     Which table a <see cref="UiFilters" /> drives: the first <c>Ui.Table.Of(…)</c> built after it in the same render,
///     as a tab list with no panels switches the first table after it.
/// </summary>
/// <remarks>Per thread, because a render is: the sets still waiting are handed the next table built, then forgotten.</remarks>
internal static class UiFilterTables
{
    [ThreadStatic]
    private static List<WeakReference<UiFilters>>? _waiting;

    /// <summary>A set was built and waits for the table after it.</summary>
    internal static void Wait(UiFilters filters) => (_waiting ??= []).Add(new WeakReference<UiFilters>(filters));

    /// <summary>A table drawn from a query was built: every set still waiting drives it.</summary>
    internal static void Built(UiTable table)
    {
        if (_waiting is not { Count: > 0 } waiting)
        {
            return;
        }

        foreach (var set in waiting)
        {
            if (set.TryGetTarget(out var filters))
            {
                filters.Drives(table);
            }
        }

        waiting.Clear();
    }
}
