namespace Rask;

/// <summary>
///     What a <c>.Choices(…)</c> made: what it was handed, the options drawn now, the search that answers with rows,
///     and whether choosing nothing is an answer — and, for a search that pages, the page after the rows found so far.
/// </summary>
/// <param name="Said">What <c>.Choices(…)</c> was handed, for the next render to compare with.</param>
/// <param name="Rows">The options drawn before the first answer.</param>
/// <param name="Find">The search; for one that pages, its first page.</param>
/// <param name="MayBeEmpty">Whether choosing nothing is an answer.</param>
/// <param name="Next">For a search that pages: so many rows (the third) after the first so many (the second), for what was typed.</param>
internal sealed record UiChosen(
    object Said,
    Component?[] Rows,
    Fn<string, Task<IEnumerable<Component?>>>? Find,
    bool MayBeEmpty,
    Func<string, int, int, Task<IEnumerable<Component?>>>? Next = null);
