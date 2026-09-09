namespace Rask.Dashboard;

/// <summary>
/// The one daisyUI theme the console is painted with.
/// </summary>
/// <remarks>
/// <para>
/// Named once because it has to be written in two places and they must not drift. daisyUI applies a
/// theme through <c>data-theme</c> on the element carrying the kit's theme scope, and the console has
/// two such elements: <c>&lt;html&gt;</c>, so the <c>--color-ui-*</c> aliases in <c>dashboard.css</c>
/// resolve at <c>:root</c> where Tailwind emits them (<see cref="RaskDashboardShell" />), and the
/// <c>UiShell</c> div, because <c>[data-rask-ui]:not([data-theme])</c> would otherwise redefine
/// <c>--color-base-*</c> for everything inside it whenever the operator's OS is in dark mode.
/// </para>
/// <para>
/// Light, and deliberately not a setting. The console's stylesheet is a set of contrast ratios measured
/// against a white ground, and daisyUI's warning and error are surface colours that fail as text on one
/// — the <c>-ink</c> twins in that file exist to correct exactly that, and they are derived for light.
/// Making the palette follow the host application or the reader's OS would invalidate every one of
/// those measurements silently, which is how the console spent a release painting its chrome dark and
/// its labels near-black on any machine set to dark mode.
/// </para>
/// </remarks>
internal static class DashboardTheme
{
    /// <summary>daisyUI's <c>light</c>.</summary>
    public const UiThemeName Name = UiThemeName.Light;
}
