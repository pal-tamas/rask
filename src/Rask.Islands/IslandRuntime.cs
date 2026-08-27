namespace Rask.Islands;

/// <summary>
///     Which adapter mounts an island.
/// </summary>
/// <remarks>
///     The framework is not the primitive; the adapter is. Every runtime is reached through the same
///     three functions — <c>mount</c>, <c>update</c>, <c>unmount</c> — which is what makes the second
///     and third cheap once the first exists.
/// </remarks>
public enum IslandRuntime
{
    /// <summary>Read the runtime from the module's extension. The default.</summary>
    Infer = 0,

    /// <summary>
    ///     React, and Preact unchanged.
    /// </summary>
    /// <remarks>
    ///     One value covers both because nothing distinguishes them at the call site or in the file
    ///     extension: a Preact project aliases <c>react</c> and <c>react-dom</c> to
    ///     <c>preact/compat</c> in tsconfig and in the Vite plugin, so the same adapter type-checks and
    ///     bundles against either. Rask does not need to know which one it got.
    /// </remarks>
    React = 1,

    /// <summary>
    ///     Lit, and any other component that is really a custom element.
    /// </summary>
    /// <remarks>
    ///     The cheapest runtime of the set, and the only one that ships no adapter code: mounting is
    ///     <c>createElement</c> plus property assignment, updating is the same assignment, unmounting
    ///     is <c>remove()</c>. Reactive properties re-render on assignment with nothing in between.
    /// </remarks>
    Lit = 2,
}
