namespace Rask.Core.Browser;

/// <summary>Whether the page is currently visible to the user (the Page Visibility API).</summary>
public enum PageVisibility
{
    /// <summary>The page is at least partially visible (foreground tab, not minimized).</summary>
    Visible,

    /// <summary>The page is not visible (background tab, minimized window, or device locked).</summary>
    Hidden,

    /// <summary>The page is being pre-rendered and is not yet visible.</summary>
    Prerender
}
