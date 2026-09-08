namespace Rask.Core.Live;

/// <summary>
///     Supplies the concrete paths for a route a prerender pass cannot enumerate on its own.
/// </summary>
/// <remarks>
///     <para>
///         A pass walks the route table and keeps the routes whose every segment is a literal, because a
///         parameterised one has no path without data. That is correct and it is also where most of a
///         real site lives: a documentation site's <c>/guides/{slug}</c> is one route and eighty pages,
///         and every one of them ships as an empty boot shell to a crawler unless something says what the
///         slugs are.
///     </para>
///     <para>
///         The app is the only thing that knows. Register an implementation and the pass renders its
///         paths alongside the literal ones — same waves, same skip rules, same sitemap. Paths are
///         rooted and carry no <c>PathBase</c>: they are route paths, exactly as
///         <c>RouteState.Path</c> holds them, so a generated <c>Routes.X(value)</c> helper is what to
///         return rather than an interpolated string.
///     </para>
///     <para>
///         Several implementations may be registered; their paths are concatenated, and anything already
///         in the literal plan is ignored rather than rendered twice.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public sealed class GuidePaths : IPrerenderPaths
///     {
///         public IEnumerable&lt;string&gt; Paths() =>
///             GuideCatalog.All.Select(guide =&gt; Routes.GuidePage(guide.Slug));
///     }
///
///     // in Program.cs
///     host.Services.AddSingleton&lt;IPrerenderPaths, GuidePaths&gt;();
///     </code>
/// </example>
public interface IPrerenderPaths
{
    /// <summary>The paths to render, rooted and without a <c>PathBase</c>.</summary>
    IEnumerable<string> Paths();
}
