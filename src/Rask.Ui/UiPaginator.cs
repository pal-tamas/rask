namespace Rask;

/// <summary>
///     What <see cref="UiPagination" /> pages through: where a list is, and how far it runs.
/// </summary>
/// <remarks>
///     <para>
///     Flux hands its pagination a Laravel paginator, and this is what one knows. With a <see cref="Total" />
///     it is <c>Order::paginate()</c>: the pager counts the pages, numbers them and says
///     "Showing 16 to 30 of 240 results". Without one it is <c>Order::simplePaginate()</c>, for a list too
///     long to count — only Previous and Next, and <see cref="HasMore" /> says whether Next leads anywhere.
///     </para>
///     <code>
///     new UiPaginator { Page = 2, PerPage = 15, Total = 240 }
///     new UiPaginator { Page = 2, PerPage = 15, HasMore = true }
///     </code>
/// </remarks>
public sealed record UiPaginator
{
    /// <summary>The page being shown, counted from one.</summary>
    public int Page { get; init; } = 1;

    /// <summary>How many results a page holds. Fifteen, as Laravel's is.</summary>
    public int PerPage { get; init; } = 15;

    /// <summary>How many results there are in all. Unset makes this the simple paginator.</summary>
    public int? Total { get; init; }

    /// <summary>For the simple paginator: whether a page follows this one. A counted one works it out.</summary>
    public bool HasMore { get; init; }

    /// <summary>The page this one is on, held inside the pages there are.</summary>
    internal int Current => LastPage is { } last ? Math.Clamp(Page, 1, last) : Math.Max(Page, 1);

    /// <summary>The last page, when the results are counted. An empty list still has its first page.</summary>
    internal int? LastPage => Total is { } total ? Math.Max(1, (Math.Max(total, 0) + Size - 1) / Size) : null;

    /// <summary>The first result on this page, counted from one; zero when there are none.</summary>
    internal int From => Total is <= 0 ? 0 : ((Current - 1) * Size) + 1;

    /// <summary>The last result on this page.</summary>
    internal int To => Math.Min(Current * Size, Math.Max(Total ?? 0, 0));

    internal bool OnFirstPage => Current <= 1;

    internal bool OnLastPage => LastPage is { } last ? Current >= last : !HasMore;

    private int Size => Math.Max(PerPage, 1);
}
