using Microsoft.Extensions.Primitives;
using Rask.Core.Live;

namespace Rask.Core.Routing;

public readonly record struct RouteUrl(string Path, string? QueryString = null, Type? PageType = null)
{
    // The language this URL was asked for in (In), or null for the language of the page it is used on.
    internal string? Language { get; init; }

    public static RouteUrl External(string url) => new(url);

    /// <summary>
    ///     This page in another language — <c>Routes.About().In("de")</c> is <c>/de/about</c> — for a language
    ///     switcher in an app that puts languages in its URLs (<c>UseCulture(c =&gt; c.UrlPrefix = true)</c>).
    /// </summary>
    /// <remarks>
    ///     Without <c>.In</c> a generated URL keeps the language of the page it is used on. The default language
    ///     is the root's (<c>/about</c>), and so is a language the app does not ship. In an app that keeps
    ///     languages out of its URLs this changes nothing. The page being shown, in another language, is
    ///     <c>route.In("de")</c> on the injected <see cref="RouteState" />.
    /// </remarks>
    /// <param name="language">A language the app ships, as a BCP&#160;47 tag: <c>"de"</c>.</param>
    public RouteUrl In(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return this with { Language = language };
    }

    /// <summary>
    ///     This URL with <paramref name="key" /> set in its query, every other key kept — the link that opens a list's
    ///     editor over it: <c>Routes.DriversPage().With("edit", "new")</c> is <c>/drivers?edit=new</c>.
    /// </summary>
    /// <remarks>
    ///     A <c>null</c> value takes the key out. A key already there is replaced, matched without regard to case; a
    ///     new one goes last. The page being shown with a key changed is <c>route.With(…)</c> on the injected
    ///     <see cref="RouteState" />.
    /// </remarks>
    /// <param name="key">The query parameter: <c>"edit"</c>.</param>
    /// <param name="value">Its value, escaped as it is written; <c>null</c> removes it.</param>
    /// <returns>The same page, language and path, with the query changed.</returns>
    public RouteUrl With(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this with { QueryString = Changed(Routing.QueryString.Parse(QueryString), [new(key, value)]) };
    }

    /// <summary>
    ///     <paramref name="query" /> with <paramref name="changes" /> written into it — a pair with no value takes its key
    ///     out, every other key stays — as a query string (<c>?a=1&amp;b=2</c>), or <c>null</c> when nothing is left.
    /// </summary>
    internal static string? Changed(IEnumerable<KeyValuePair<string, StringValues>> query, IEnumerable<KeyValuePair<string, string?>> changes)
    {
        var merged = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in query)
        {
            merged[key] = value;
        }

        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                merged.Remove(key);
            }
            else
            {
                merged[key] = value;
            }
        }

        var written = Routing.QueryString.Build("", merged);
        return written.Length == 0 ? null : written;
    }

    public override string ToString()
    {
        var path = AddressIn(null);
        return string.IsNullOrEmpty(QueryString) ? path : path + QueryString;
    }

    /// <summary>
    ///     <see cref="Path" /> as the browser addresses it: with the language prefix when the app puts languages in
    ///     its URLs, otherwise the path itself.
    /// </summary>
    /// <remarks>
    ///     A generated URL, or one asked for in a language, is this app's page, so it takes a prefix; a string
    ///     converted to a <see cref="RouteUrl" /> is used as written. With no language asked for, the prefix is the
    ///     current page's, so a component that writes one depends on it as it does on the culture: it is marked
    ///     as reading ambient state, and a language switch repaints it.
    /// </remarks>
    /// <param name="route">The route to read the languages from, or <c>null</c> for the work in progress's.</param>
    internal string AddressIn(RouteState? route)
    {
        if (!UrlLanguages.IsEnabled || Path is null || (PageType is null && Language is null))
        {
            return Path!;
        }

        route ??= UrlLanguages.CurrentRoute;
        if (route?.Languages is not { } languages)
        {
            return Path;
        }

        if (Language is null)
        {
            LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();
        }

        return languages.Join(Language ?? route.Language, Path);
    }

    /// <summary>
    ///     Navigates here, from an event handler or a save — <c>Routes.ProductsPage().Go()</c> — with nothing
    ///     injected. Works inside a component, where the page's bare name is its chain entry rather than its type.
    /// </summary>
    /// <returns>The step that replaces the current history entry instead: <c>.Go().Replacing()</c>.</returns>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public GoTo Go() => global::Rask.Core.Go.To(this);

    public static implicit operator RouteUrl(string url) => new(url);
    public static implicit operator string(RouteUrl url) => url.ToString();
}
