using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     Where the user goes next, from an event handler: <c>Go.To("/products/42")</c>, or the current page with a
///     changed query, <c>Go.With("page", "2")</c>. A typed route goes on its own: <c>Routes.ProductPage(42).Go()</c>.
/// </summary>
/// <remarks>
///     It works while a handler runs — the URL is pushed into the browser's history after the handler
///     returns — and in a page's <c>OnMount</c> or <c>OnUpdated</c> before the first <c>await</c>, where it is a
///     redirect: the reader lands on the destination and sees nothing of the page that sent them on. Anywhere
///     else it throws, saying so.
/// </remarks>
public static class Go
{
    /// <summary>Goes to <paramref name="path" />, dropping the current query.</summary>
    /// <param name="path">Where to go: <c>"/users/42"</c>.</param>
    /// <returns>The step that makes it replace the current history entry: <c>Go.To("/login").Replacing()</c>.</returns>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static GoTo To(string path)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.NavigateTo(path);
        return new GoTo(navigator);
    }

    /// <summary>Goes to <paramref name="url" />, its path and query together.</summary>
    /// <param name="url">A typed route's URL: <c>Routes.ProductPage(42)</c>.</param>
    /// <returns>The step that makes it replace the current history entry.</returns>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
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
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static GoTo To(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var navigator = Navigator.RequireCurrent();
        navigator.NavigateTo(path, query);
        return new GoTo(navigator);
    }

    /// <summary>Stays on this page with <paramref name="key" /> set in its query; a <c>null</c> value removes it.</summary>
    /// <param name="key">The query parameter, matched without regard to case.</param>
    /// <param name="value">Its new value.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void With(string key, string? value) => Navigator.RequireCurrent().SetQuery(key, value);

    /// <summary>Stays on this page with each of <paramref name="values" /> set in its query, in one step.</summary>
    /// <param name="values">The parameters; one whose value is <c>null</c> is removed.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void With(params KeyValuePair<string, string?>[] values) => Navigator.RequireCurrent().SetQuery(values);

    /// <summary>Stays on this page without <paramref name="key" /> in its query.</summary>
    /// <param name="key">The query parameter, matched without regard to case.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void Without(string key) => Navigator.RequireCurrent().RemoveQuery(key);

    /// <summary>Stays on this page without any query.</summary>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void Without() => Navigator.RequireCurrent().ClearQuery();
}
