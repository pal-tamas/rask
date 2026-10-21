using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     Where the user goes next, from an event handler: <c>Go.To("/products/42")</c>, or the current page with a
///     changed query, <c>Go.With("page", "2")</c>, which takes the current history entry's place. A typed route goes on
///     its own: <c>Routes.ProductPage(42).Go()</c>.
/// </summary>
/// <remarks>
///     It works while a handler runs — the URL is pushed into the browser's history after the handler returns —
///     and in a page's <c>OnMount</c> or <c>OnUpdated</c>, where it is a redirect: the reader lands on the
///     destination, which replaces the page that sent them on. With no session handling anything — a background
///     task, a component rendered with no host — it throws, saying so.
/// </remarks>
public static class Go
{
    /// <summary>Goes to <paramref name="path" />, dropping the current query.</summary>
    /// <param name="path">Where to go: <c>"/users/42"</c>.</param>
    /// <returns>The step that makes it replace the current history entry: <c>Go.To("/login").Replacing()</c>.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo To(string path)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.NavigateTo(path);
        return new GoTo(navigator);
    }

    /// <summary>Goes to <paramref name="url" />, its path and query together.</summary>
    /// <param name="url">A typed route's URL: <c>Routes.ProductPage(42)</c>.</param>
    /// <returns>The step that makes it replace the current history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo To(RouteUrl url)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.NavigateTo(url);
        return new GoTo(navigator);
    }

    /// <summary>Goes to <paramref name="path" /> with <paramref name="query" /> as its whole query.</summary>
    /// <param name="path">Where to go.</param>
    /// <param name="query">The query; a pair whose value is <c>null</c> is left out, a repeated key keeps every value.</param>
    /// <returns>The step that makes it replace the current history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo To(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.NavigateTo(path, query);
        return new GoTo(navigator);
    }

    /// <summary>
    ///     Leaves the app for <paramref name="path" />, a page of this site that the app does not render — one the
    ///     app's path base does not cover: <c>Go.Out("/tenants")</c> from an app mapped under <c>/new</c>.
    /// </summary>
    /// <remarks>
    ///     The address is used as written, with no path base in front of it, and the browser loads it as a page:
    ///     a <c>302</c> on the first request, a full-page navigation from a handler or a lifecycle hook. Only a
    ///     path on this site is taken — another site, a scheme, <c>//</c> and <c>/\</c> are refused — so pass what
    ///     the app wrote, never a value from the query string that <see cref="LocalUrl.Sanitize" /> has not seen.
    /// </remarks>
    /// <param name="path">A path on this site, starting with one <c>/</c>: <c>"/tenants"</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="path" /> could lead off this site.</exception>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static void Out(string path) => Navigator.RequireCurrent().NavigateOut(path);

    /// <summary>Stays on this page with <paramref name="key" /> set in its query; a <c>null</c> value removes it.</summary>
    /// <remarks>
    ///     A move that changes only the query replaces the current history entry, as Livewire's <c>#[Url]</c> does, so a
    ///     live search does not leave a Back stop at every pause; <c>.Pushing()</c> adds one instead.
    /// </remarks>
    /// <param name="key">The query parameter, matched without regard to case.</param>
    /// <param name="value">Its new value.</param>
    /// <returns>The step that makes it add a history entry: <c>Go.With("page", "2").Pushing()</c>.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo With(string key, string? value)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.SetQuery(key, value);
        return new GoTo(navigator);
    }

    /// <summary>
    ///     Stays on this page with <paramref name="key" /> set to a list — one key, its elements joined by commas,
    ///     as a <c>[QueryParam] IReadOnlyList&lt;T&gt;</c> reads it: <c>Go.With("drivers", ["ada", "theo"])</c>. A
    ///     list with no element removes the key.
    /// </summary>
    /// <remarks>Replaces the current history entry; <c>.Pushing()</c> adds one instead.</remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="key">The query parameter, matched without regard to case.</param>
    /// <param name="values">Its elements, in order; a null one, or one that writes as nothing, is left out.</param>
    /// <returns>The step that makes it add a history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo With<T>(string key, IEnumerable<T>? values) => With(key, QueryList.Value(values));

    /// <summary>Stays on this page with each of <paramref name="values" /> set in its query, in one step.</summary>
    /// <remarks>Replaces the current history entry; <c>.Pushing()</c> adds one instead.</remarks>
    /// <param name="values">The parameters; one whose value is <c>null</c> is removed.</param>
    /// <returns>The step that makes it add a history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo With(params KeyValuePair<string, string?>[] values)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.SetQuery(values);
        return new GoTo(navigator);
    }

    /// <summary>Stays on this page without <paramref name="key" /> in its query.</summary>
    /// <remarks>Replaces the current history entry; <c>.Pushing()</c> adds one instead.</remarks>
    /// <param name="key">The query parameter, matched without regard to case.</param>
    /// <returns>The step that makes it add a history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo Without(string key)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.RemoveQuery(key);
        return new GoTo(navigator);
    }

    /// <summary>Stays on this page without any query.</summary>
    /// <remarks>Replaces the current history entry; <c>.Pushing()</c> adds one instead.</remarks>
    /// <returns>The step that makes it add a history entry.</returns>
    /// <exception cref="InvalidOperationException">Called with no session handling an event or mounting a page.</exception>
    public static GoTo Without()
    {
        var navigator = Navigator.RequireCurrent();
        navigator.ClearQuery();
        return new GoTo(navigator);
    }
}
