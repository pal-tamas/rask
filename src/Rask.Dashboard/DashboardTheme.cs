namespace Rask.Dashboard;

/// <summary>
/// The one daisyUI theme the console is painted with.
/// </summary>
/// <remarks>
/// <para>
/// Named once because it has to be written in two places and they must not drift. daisyUI applies a
/// theme through <c>data-theme</c> on the element carrying the kit's theme scope, and the console has
/// two such elements: <c>&lt;html&gt;</c>, so the page ground the kit's console reset paints on the body
/// from <c>--color-base-200</c> resolves there (<see cref="RaskDashboardShell" />), and the <c>UiShell</c>
/// div, because <c>[data-rask-ui]:not([data-theme])</c> would otherwise redefine <c>--color-base-*</c> for
/// everything inside it whenever the operator's OS is in dark mode.
/// </para>
/// <para>
/// Light, and deliberately not a setting. An operator surface is a set of contrast ratios checked against
/// one ground, and making the palette follow the host application or the reader's OS would move every one
/// of them silently — which is how the console once spent a release painting its chrome dark and its labels
/// near-black on any machine set to dark mode.
/// </para>
/// </remarks>
internal static class DashboardTheme
{
    /// <summary>daisyUI's <c>light</c>.</summary>
    public const UiThemeName Name = UiThemeName.Light;
}
