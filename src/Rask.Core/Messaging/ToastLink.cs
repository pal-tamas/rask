using Rask.Core.Routing;

namespace Rask.Core.Messaging;

/// <summary>The link a toast carries under its message: what it says, and where it goes.</summary>
/// <param name="Label">The link's text — <c>"View invoice"</c>.</param>
/// <param name="Href">Where it goes.</param>
public sealed record ToastLink(string Label, RouteUrl Href);
