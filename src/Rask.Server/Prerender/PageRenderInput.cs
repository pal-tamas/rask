using System.Security.Claims;
using Rask.Core.Globalization;
using QueryCollection = Rask.Core.Routing.QueryCollection;

namespace Rask.Server.Prerender;

/// <summary>What a page is rendered for.</summary>
/// <param name="Path">The route path, with any <c>PathBase</c> already removed.</param>
/// <param name="Query">The query the page sees through <c>RouteState</c>.</param>
/// <param name="User">The principal the page renders for.</param>
/// <param name="Culture">The negotiated culture, or <c>null</c> when the app configured no languages.</param>
/// <param name="Chain">The resolved route chain, outermost layout first.</param>
/// <param name="NotFoundPage">The not-found page the path fell through to, or <c>null</c>.</param>
internal readonly record struct PageRenderInput(
    string Path,
    QueryCollection Query,
    ClaimsPrincipal User,
    CultureNegotiation? Culture,
    IReadOnlyList<Type> Chain,
    Type? NotFoundPage);
