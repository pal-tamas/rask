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
///     names its key here as it renders, and the way back leaves it out, so going back never reopens a form.
/// </remarks>
internal sealed class UiListAddresses
{
    private static readonly ConditionalWeakTable<RouteState, UiListAddresses> Sessions = new();

    private readonly Lock _gate = new();
    private readonly Dictionary<string, KeyValuePair<string, StringValues>[]> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dialogs = new(StringComparer.Ordinal);
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
            return page with { QueryString = RouteUrl.Changed(query.Where(pair => !_dialogs.Contains(pair.Key)), []) };
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
