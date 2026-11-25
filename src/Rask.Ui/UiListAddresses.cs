using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Where each page was last shown in this session, its query kept — a list's filters, its sort, its page — so the
///     <see cref="UiAppFrame" />'s breadcrumbs lead back to a list as the reader left it, not to the bare list.
/// </summary>
/// <remarks>
///     One per session: keyed by the session's <see cref="RouteState" />, noted on every move of the address, and gone with
///     the session. The key of an <see cref="UiEditorDialog" /> (<c>?edit=</c>) is no part of where a list stands: a dialog
///     names its key here as it renders, and the way back leaves it out, so going back never reopens a form. Nor is the
///     address the reader stands at a way back: a list's own dialog that names no key here is closed by the list as given.
/// </remarks>
internal sealed class UiListAddresses
{
    private static readonly ConditionalWeakTable<RouteState, UiListAddresses> Sessions = new();

    private readonly Lock _gate = new();
    private readonly Dictionary<string, KeyValuePair<string, StringValues>[]> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dialogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly RouteState _route;

    private UiListAddresses(RouteState route)
    {
        _route = route;
        Note();
        route.Changed += (_, _) => Note();
    }

    /// <summary>The session's memory, made on its first ask.</summary>
    internal static UiListAddresses Of(RouteState route) => Sessions.GetValue(route, static session => new UiListAddresses(session));

    /// <summary>Names a query key that opens a dialog over a page, so the way back leaves it out.</summary>
    internal void Dialog(string key)
    {
        lock (_gate)
        {
            _dialogs.Add(key);
        }
    }

    /// <summary>Whether <paramref name="key" /> opens a dialog over a page: it says nothing of what a list holds.</summary>
    internal bool IsDialog(string key)
    {
        lock (_gate)
        {
            return _dialogs.Contains(key);
        }
    }

    /// <summary>Names the query key a list on the page the reader stands at keeps its page under (<c>Ui.Table.PageIn(…)</c>).</summary>
    internal void Paged(string key)
    {
        var path = Key(_route.Path);
        lock (_gate)
        {
            if (!_pages.TryGetValue(path, out var keys))
            {
                _pages[path] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            keys.Add(key);
        }
    }

    /// <summary>
    ///     The page keys of the lists on the page the reader stands at that its address holds: what a move to another
    ///     tab takes out, so the list starts at its first page.
    /// </summary>
    internal string[] PagesHeld()
    {
        var path = Key(_route.Path);
        lock (_gate)
        {
            return _pages.TryGetValue(path, out var keys) ? [.. keys.Where(_route.Query.ContainsKey)] : [];
        }
    }

    /// <summary>Where <paramref name="page" /> was last shown, its query kept; the page as given when it was not shown yet.</summary>
    internal RouteUrl Back(RouteUrl page)
    {
        lock (_gate)
        {
            if (!_last.TryGetValue(Key(page.Path), out var query))
            {
                return page;
            }

            // Written as any address the router writes, a dialog's key left out.
            var kept = query.Where(pair => !_dialogs.Contains(pair.Key)).ToArray();

            // Where the reader stands is no way back: on the list itself, under a dialog of the app's own that named no
            // key here, it would be a link to the open dialog, which leaves nothing. The list as given closes it.
            return kept.Length == query.Length && string.Equals(Key(page.Path), Key(_route.Path), StringComparison.OrdinalIgnoreCase)
                ? page
                : page with { QueryString = RouteUrl.Changed(kept, []) };
        }
    }

    private void Note()
    {
        var query = _route.Query.ToArray();
        lock (_gate)
        {
            _last[Key(_route.Path)] = query;
        }
    }

    private static string Key(string path) => path.TrimEnd('/');
}
