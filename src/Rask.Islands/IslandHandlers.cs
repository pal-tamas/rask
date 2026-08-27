using Rask.Core;

namespace Rask.Islands;

/// <summary>
///     Gives an island's generated code a handler id for a delegate prop.
/// </summary>
/// <remarks>
///     <para>
///         A thin seam over <c>Component.RegisterHandler</c>, which is <c>internal</c> to Rask.Core.
///         The generated partial lives in the <em>app's</em> assembly, which has no access to Core's
///         internals — but this package does, so the seam lives here rather than widening Core's public
///         surface for one caller.
///     </para>
///     <para>
///         The id is the same one <c>data-rask-on-click</c> uses, and it carries the same guarantee:
///         stable per (component, slot) across re-renders. That stability is load-bearing for an
///         island. The client runtime keys its function cache on this id, so a callback handed to
///         React keeps its identity between updates — without it every render would hand React a
///         fresh function, invalidating any <c>useCallback</c> or <c>memo</c> that depends on it and
///         re-firing every <c>useEffect</c> that lists it.
///     </para>
/// </remarks>
public static class IslandHandlers
{
    /// <summary>Registers <paramref name="handler" /> against <paramref name="owner" />'s next slot.</summary>
    /// <param name="owner">The island the delegate was declared on.</param>
    /// <param name="handler">The delegate prop.</param>
    /// <returns>The handler id to write into the props JSON.</returns>
    public static string Register(Component owner, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        return owner.RegisterHandler(handler);
    }
}
