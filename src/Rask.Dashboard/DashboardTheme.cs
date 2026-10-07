namespace Rask.Dashboard;

/// <summary>
/// The one daisyUI theme the console is painted with.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI applies a theme through <c>data-theme</c> on the element carrying the kit's theme scope, which
/// for the console is <c>&lt;html&gt;</c> (<see cref="RaskDashboardShell" />). Unnamed,
/// <c>[data-rask-ui]:not([data-theme])</c> would repaint everything inside it whenever the operator's OS
/// is in dark mode.
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
    public const Ui.ThemeName Name = Ui.ThemeName.Light;
}
