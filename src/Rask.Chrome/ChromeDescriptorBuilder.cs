using Rask.Chrome.Components;
using Rask.Core;

namespace Rask.Chrome;

/// <summary>
///     Turns the portable bars into the descriptor a native head reads.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of the chrome pipeline that is not native-specific, and it lives here so every
///         hosting model can run it. In the in-process model the native session calls it; in the remote
///         models the server or WASM session does, because those are the sessions that render the tree —
///         and they cannot see <c>Rask.Native</c>, nor should they.
///     </para>
///     <para>
///         <c>Rask.Native</c> keeps its own half: <c>NativeHeaderBar</c>'s segments, <c>NativeToolbar</c>,
///         the back and overflow-menu items, and the colour model. Those are platform-exact features with
///         no portable counterpart, and a server app cannot name them anyway.
///     </para>
///     <para>
///         Colours arrive as already-resolved tokens rather than a theme object: the portable bars carry no
///         appearance of their own, so whichever session is building takes the style from wherever it keeps
///         it, and this stays free of any host's colour type.
///     </para>
/// </remarks>
internal static class ChromeDescriptorBuilder
{
    /// <summary>
    ///     Describe an <see cref="AppBar" />, or return null if this component is not one — a caller with a
    ///     platform-exact bar handles that itself.
    /// </summary>
    /// <param name="header">The bar the render walk collected.</param>
    /// <param name="handlers">
    ///     Tap ids to callbacks, filled as items are described. The head echoes an id back when the item is
    ///     tapped, which is how a bar button that lives in C# runs from a real platform control.
    /// </param>
    /// <param name="background">Resolved background token, or null to leave it to the platform.</param>
    /// <param name="tint">Resolved tint token.</param>
    /// <param name="titleColor">Resolved title-colour token.</param>
    public static NativeHeaderDescriptor? BuildHeader(
        Component? header,
        Dictionary<string, Action> handlers,
        string? background = null,
        string? tint = null,
        string? titleColor = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        if (header is not AppBar appBar)
        {
            return null;
        }

        var descriptor = new NativeHeaderDescriptor
        {
            Title = appBar.Title,
            Background = background,
            Tint = tint,
            TitleColor = titleColor,
        };

        if (appBar.Leading is { } leading)
        {
            descriptor.Leading = BuildItem(leading, "h.leading", handlers);
        }

        if (appBar.Trailing is { Count: > 0 } trailing)
        {
            descriptor.Trailing = new List<NativeBarItemDescriptor>(trailing.Count);
            for (var i = 0; i < trailing.Count; i++)
            {
                descriptor.Trailing.Add(BuildItem(trailing[i], "h.trailing." + i, handlers));
            }
        }

        return descriptor;
    }

    /// <summary>
    ///     Describe a <see cref="TabStrip" />, or return null if this component is not one.
    /// </summary>
    /// <param name="footer">The bar the render walk collected.</param>
    /// <param name="currentPath">
    ///     The route being shown, so the right tab lights without the app restating it. Uses the strip's own
    ///     <c>DeriveSelected</c>, so a tab highlights identically on the web and on a platform tab bar.
    /// </param>
    /// <param name="background">Resolved background token.</param>
    /// <param name="tint">Resolved tint token for the selected tab.</param>
    /// <param name="unselectedTint">Resolved tint token for the rest.</param>
    public static NativeFooterDescriptor? BuildFooter(
        Component? footer,
        string currentPath,
        string? background = null,
        string? tint = null,
        string? unselectedTint = null)
    {
        if (footer is not TabStrip strip)
        {
            return null;
        }

        var descriptor = new NativeFooterDescriptor
        {
            Kind = "tabbar",
            Selected = strip.Selected ?? TabStrip.DeriveSelected(strip.Tabs, currentPath),
            Background = background,
            Tint = tint,
            UnselectedTint = unselectedTint,
        };

        if (strip.Tabs is { Count: > 0 } tabs)
        {
            descriptor.Tabs = new List<NativeTabDescriptor>(tabs.Count);
            foreach (var tab in tabs)
            {
                descriptor.Tabs.Add(new NativeTabDescriptor
                {
                    Title = tab.Title,
                    IosIcon = tab.Icon.IosSymbol,
                    AndroidIcon = tab.Icon.AndroidResource,
                    Path = tab.To.ToString(),
                    Badge = string.IsNullOrEmpty(tab.Badge) ? null : tab.Badge,
                });
            }
        }

        return descriptor;
    }

    /// <summary>
    ///     Describe a portable <see cref="BarButton" />. An item with no <c>OnClick</c> carries no id, so
    ///     the head knows there is nothing to echo back and can render it inert.
    /// </summary>
    public static NativeBarItemDescriptor BuildItem(
        BarButton button, string id, Dictionary<string, Action> handlers)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(handlers);

        string? tapId = null;
        if (button.OnClick is { } onClick)
        {
            handlers[id] = onClick;
            tapId = id;
        }

        return new NativeBarItemDescriptor
        {
            Kind = "button",
            Id = tapId,
            IosIcon = button.Icon.IosSymbol,
            AndroidIcon = button.Icon.AndroidResource,
            Title = button.Title,
        };
    }
}
