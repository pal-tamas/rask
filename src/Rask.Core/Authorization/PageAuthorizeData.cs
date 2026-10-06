using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;

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
    }

    public bool AllowsAnonymous { get; }

    public IAuthorizeData[] Data { get; }

    public static PageAuthorizeData Of(Type page) => ByPage.GetOrAdd(page, static type => new PageAuthorizeData(type));

    public static void Forget() => ByPage.Clear();
}
