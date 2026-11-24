namespace Rask.Data;

/// <summary>
///     One row a declared projection saw change in a save — <c>b.Projects&lt;E&gt;(…)</c>: what it held before, and what
///     it holds now.
/// </summary>
/// <typeparam name="T">The row's type.</typeparam>
/// <param name="Before">
///     The row as it was stored before this save — a detached copy holding its stored values — or
///     <see langword="null" /> for a row this save adds.
/// </param>
/// <param name="After">The row as this save writes it — the tracked row itself — or <see langword="null" /> for a row it removes.</param>
public sealed record ProjectedRow<T>(T? Before, T? After)
    where T : class
{
    /// <summary>Whether this save adds the row.</summary>
    public bool Added => Before is null;

    /// <summary>Whether this save removes the row.</summary>
    public bool Removed => After is null;
}
