using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Core.Authorization;

/// <summary>
///     What a page type declares about who may open it, read from its attributes once.
/// </summary>
/// <remarks>
///     The guard asks on every request and every event, and reflection builds each attribute afresh every
///     time it is asked. A type's attributes only change under hot reload, which calls <see cref="Forget" />.
/// </remarks>
internal sealed class PageAuthorizeData
{
    private static readonly ConcurrentDictionary<Type, PageAuthorizeData> ByPage = new();

    private PageAuthorizeData(Type page)
    {
        AllowsAnonymous = page.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null;
        Data = [.. page.GetCustomAttributes(true).OfType<IAuthorizeData>()];
        GuestsOnlyThen = page.GetCustomAttribute<GuestAttribute>(false) is { } guest ? Checked(page, guest.Then) : null;
    }

    public bool AllowsAnonymous { get; }

    /// <summary>Where a signed-in reader is sent instead (<c>[Guest]</c>); null when anybody may open the page.</summary>
    public string? GuestsOnlyThen { get; }

    public IAuthorizeData[] Data { get; }

    public static PageAuthorizeData Of(Type page) => ByPage.GetOrAdd(page, static type => new PageAuthorizeData(type));

    public static void Forget() => ByPage.Clear();

    // The address becomes a Location header, so it is held to what LocalUrl lets through: a path on this app.
    private static string Checked(Type page, string then) =>
        then is not null && string.Equals(LocalUrl.Sanitize(then), then, StringComparison.Ordinal)
            ? then
            : throw new InvalidOperationException(
                $"[Guest(Then = \"{then}\")] on {page.Name} must name a page of this app: one '/' and then the path, " +
                "like \"/app\". Another site, a scheme, '//' or '/\\' is refused. See docs/authentication.md.");
}
