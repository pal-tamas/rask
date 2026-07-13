using Rask.Core.Routing;

namespace Rask.Native.Components;

/// <summary>
///     A single tab in a <see cref="NativeTabBar" /> — an icon + label that navigates to a route when tapped.
///     Use a type-safe route from <c>Features.Routes.*</c> for <see cref="To" />.
/// </summary>
public sealed class NativeTab : NativeBarItem
{
    /// <summary>The tab's label. Required.</summary>
    public required string Title { get; set; }

    /// <summary>The tab's icon. Required.</summary>
    public required NativeIcon Icon { get; set; }

    /// <summary>The type-safe route this tab navigates to (e.g. <c>Features.Routes.Home()</c>). Required.</summary>
    public required RouteUrl To { get; set; }
}
