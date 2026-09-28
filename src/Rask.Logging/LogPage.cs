namespace Rask.Logging;

/// <summary>One page of query results, plus the total the filter matched.</summary>
/// <param name="Entries">The matching entries, newest first.</param>
/// <param name="TotalCount">How many entries match the filter in total, across every page.</param>
/// <param name="Page">The 1-based page these entries came from.</param>
/// <param name="PageSize">The page size the query ran with.</param>
public sealed record LogPage(IReadOnlyList<LogRecord> Entries, long TotalCount, int Page, int PageSize)
{
    /// <summary>An empty page, for a store with nothing in it yet.</summary>
    public static LogPage Empty(int page, int pageSize) => new([], 0, page, pageSize);

    /// <summary>How many pages the filter spans, at least 1.</summary>
    public int PageCount => TotalCount <= 0 ? 1 : (int)((TotalCount + PageSize - 1) / PageSize);
}
