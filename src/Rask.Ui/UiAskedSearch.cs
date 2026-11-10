namespace Rask;

/// <summary>
///     A search a select or pillbox asks (<c>.Search(…)</c>, <c>.Choices(find, held:)</c>): the page hands it over in
///     every render, and one that is not the search the select last asked is asked again.
/// </summary>
internal static class UiAskedSearch
{
    /// <summary>
    ///     Whether <paramref name="one" /> and <paramref name="other" /> are the same search: the same method on the same
    ///     target, as delegates compare — a method group, or a lambda over nothing that changed. A lambda that closes over
    ///     a render's rows is another search in every render.
    /// </summary>
    internal static bool Same(Fn<string, Task<IEnumerable<Component?>>>? one, Fn<string, Task<IEnumerable<Component?>>>? other) =>
        EqualityComparer<Fn<string, Task<IEnumerable<Component?>>>?>.Default.Equals(one, other);

    /// <summary>Takes <paramref name="handed" /> as <paramref name="search" />, and tells <paramref name="owner" /> it changed when it is another search.</summary>
    internal static void Take(Component owner, ref Fn<string, Task<IEnumerable<Component?>>>? search, Fn<string, Task<IEnumerable<Component?>>>? handed)
    {
        if (!Same(search, handed))
        {
            BuilderRuntime.MarkChanged(owner);
        }

        search = handed;
    }
}
