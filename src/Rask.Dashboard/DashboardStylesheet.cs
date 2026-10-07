using System.Reflection;

namespace Rask.Dashboard;

/// <summary>
/// The console's compiled stylesheet: <c>Styles/dashboard.css</c>, built by Rask.Tailwind from this
/// project's sources with the kit compiled in, and embedded.
/// </summary>
/// <remarks>
/// Inlined by <see cref="Pages.DashboardLayout" /> rather than served. The console is mounted into
/// somebody else's host at <c>/_rask</c>, and a <c>&lt;link&gt;</c> would need a file in that host's
/// <c>wwwroot</c> that nothing there produces.
/// </remarks>
internal static class DashboardStylesheet
{
    /// <summary>
    /// The compiled CSS. Empty if the sheet did not ship, which leaves the console unstyled rather than
    /// unstartable; the build refuses to embed nothing (<c>EmbedRaskDashboardStylesheet</c>).
    /// </summary>
    public static string Css { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(DashboardStylesheet).GetTypeInfo().Assembly
            .GetManifestResourceStream("Rask.Dashboard.dashboard.css");
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
