using Rask.Core.Routing;

namespace Rask;

/// <summary>One row of an <see cref="UiAppFrame" />'s menu that leads to a page of the app, and the group it stands in.</summary>
internal sealed record UiAppFrameLink(UiSidebarItem Item, UiSidebarGroup? Group, RouteUrl Url, string Label)
{
    /// <summary>The row's page, with no trailing slash: the root is empty.</summary>
    internal string Path => Url.Path.TrimEnd('/');

    /// <summary>Whether <paramref name="path" /> is this row's page.</summary>
    internal bool Is(string path) => string.Equals(path.TrimEnd('/'), Path, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="path" /> is this row's page or one beneath it (its form).</summary>
    internal bool Holds(string path) => (path + "/").StartsWith(Path + "/", StringComparison.Ordinal);
}
