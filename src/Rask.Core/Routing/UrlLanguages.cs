using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     The languages an app writes into its URLs (<see cref="RaskCultureOptions.UrlPrefix" />): the default at
///     the root, every other one as the path's first segment.
/// </summary>
/// <remarks>
///     <para>
///         The prefix is to a route what the deploy's <c>PathBase</c> is: part of the address, never part of the
///         route. <see cref="RouteState.Path" /> keeps it — it is what the browser shows, and what history is
///         written from — and the router matches what is left once it is taken off, so a page declares
///         <c>[Route("/about")]</c> once and answers <c>/about</c>, <c>/en/about</c> and <c>/de/about</c>.
///     </para>
///     <para>
///         Pure string work, so it costs a runtime without culture data nothing and cannot disagree with it.
///     </para>
/// </remarks>
internal sealed class UrlLanguages
{
    private UrlLanguages(string defaultLanguage, IReadOnlyList<string> all)
    {
        Default = defaultLanguage;
        All = all;
    }

    /// <summary>
    ///     Whether any host in the process puts languages in its URLs. A fast path only, as
    ///     <see cref="RaskCulture.IsEnabled" /> is: off, a route URL never looks for a session to ask.
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>The language at the root, as the app spells it.</summary>
    public string Default { get; }

    /// <summary>Every language, the default first, as the app spells them.</summary>
    public IReadOnlyList<string> All { get; }

    /// <summary>The URL languages <paramref name="options" /> describe, or <c>null</c> when it puts none in its URLs.</summary>
    public static UrlLanguages? From(RaskCultureOptions? options)
    {
        if (options is not { UrlPrefix: true, SupportedCultures.Count: > 0 })
        {
            return null;
        }

        var supported = options.SupportedCultures;
        var fallback = options.DefaultCulture ?? supported[0];
        var @default = LanguageTag.Match(fallback, supported) ?? supported[0];

        var all = new List<string>(supported.Count) { @default };
        foreach (var name in supported.Where(name => !LanguageTag.Same(name, @default)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            all.Add(name);
        }

        return new UrlLanguages(@default, all);
    }

    /// <summary>
    ///     The language <paramref name="path" /> is in, and the path the routes know it by: <c>/en/about</c> is
    ///     <c>("en", "/about")</c>, <c>/about</c> is the default language's.
    /// </summary>
    /// <remarks>
    ///     Only a language other than the default is a prefix. <c>/hu/about</c> in an app whose default is
    ///     <c>hu</c> is not its about page — that one is <c>/about</c> — so it is left for the router to find, or not.
    /// </remarks>
    public (string Language, string RoutePath) Split(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length < 2 || path[0] != '/')
        {
            return (Default, path);
        }

        var end = path.IndexOf('/', 1);
        var segment = end < 0 ? path.AsSpan(1) : path.AsSpan(1, end - 1);
        for (var i = 1; i < All.Count; i++)
        {
            if (segment.Equals(All[i], StringComparison.OrdinalIgnoreCase))
            {
                return (All[i], end < 0 ? "/" : path[end..]);
            }
        }

        return (Default, path);
    }

    /// <summary>
    ///     The address of <paramref name="routePath" /> in <paramref name="language" />: <c>/en/about</c>, or
    ///     <c>/about</c> for the default. A language the app does not ship is the default's.
    /// </summary>
    public string Join(string? language, string routePath)
    {
        ArgumentNullException.ThrowIfNull(routePath);
        var name = Resolve(language);
        if (name is null || LanguageTag.Same(name, Default) || routePath.Length == 0 || routePath[0] != '/')
        {
            return routePath;
        }

        return routePath.Length == 1 ? "/" + name : "/" + name + routePath;
    }

    /// <summary>The app's spelling of <paramref name="language" />, or <c>null</c> when it does not ship it.</summary>
    public string? Resolve(string? language) => LanguageTag.Match(language, All);

    /// <summary>The route of the work in progress — the page rendering, or the handler running — or <c>null</c>.</summary>
    internal static RouteState? CurrentRoute =>
        LiveRenderContext.Current?.Services?.GetService(typeof(RouteState)) as RouteState
        ?? Navigator.Current?.Route;
}
