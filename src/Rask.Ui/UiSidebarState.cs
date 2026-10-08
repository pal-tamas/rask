namespace Rask;

/// <summary>The ids that join a <see cref="UiSidebar" /> to the controls that open and narrow it.</summary>
/// <remarks>
/// Flux joins them with a script and a page-wide event. Here each state is a checkbox inside the sidebar and
/// each control a <c>&lt;label&gt;</c> for it, so they work on a prerendered page with nothing running.
/// </remarks>
internal static class UiSidebarState
{
    /// <summary>The checkbox that holds "slid over the page", below the breakpoint.</summary>
    public const string Open = "sidebar-open";

    /// <summary>The checkbox that holds "narrowed to a rail", from the breakpoint up.</summary>
    public const string Rail = "sidebar-rail";

    /// <summary>Where the rail is remembered: the <c>localStorage</c> key Flux's own script keeps it under.</summary>
    public const string RailKey = "flux-sidebar-collapsed-desktop";
}
