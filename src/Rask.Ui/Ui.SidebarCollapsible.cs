namespace Rask;

public static partial class Ui
{
    /// <summary>When a <see cref="UiSidebar" /> can be put away. Flux's <c>collapsible</c>.</summary>
    public enum SidebarCollapsible
    {
        /// <summary>Never: the sidebar is beside the page at every width. Flux's <c>false</c>.</summary>
        Never,

        /// <summary>Below the breakpoint it slides over the page; from it up it is always docked.</summary>
        Mobile,

        /// <summary>As <see cref="Mobile" />, and the docked sidebar narrows to a rail of icons. Flux's <c>true</c>.</summary>
        Always,
    }
}
