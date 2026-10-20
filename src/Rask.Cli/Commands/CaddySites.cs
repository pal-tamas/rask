namespace Rask.Cli.Commands;

/// <summary>
/// Where the shared proxy's config lives on the box. The base Caddyfile only imports one file per app, so a deploy
/// writes its own app's file and never another's: two apps deploying at once cannot overwrite each other's routes.
/// The app files live on the proxy's data volume, so they outlive the proxy container when it is replaced.
/// </summary>
internal static class CaddySites
{
    /// <summary>The file Caddy starts from and <c>caddy reload</c> reads.</summary>
    internal const string BasePath = "/etc/caddy/Caddyfile";

    /// <summary>One <c>&lt;app&gt;.caddy</c> per app, on the <c>rask-caddy-data</c> volume.</summary>
    internal const string SitesDirectory = "/data/rask/sites";

    internal const string FragmentExtension = ".caddy";

    /// <summary>The line that makes a base Caddyfile the per-app layout.</summary>
    internal const string ImportLine = "import " + SitesDirectory + "/*" + FragmentExtension;

    /// <summary>
    /// The base Caddyfile a deploy writes when it moves a box to the per-app layout. Global options an operator
    /// adds above the import are kept: a later deploy only checks the import is there.
    /// </summary>
    internal const string BaseCaddyfile =
        "# Written by rask deploy. Each app's sites are in " + SitesDirectory + "/<app>" + FragmentExtension + ".\n"
        + "# Global options go above the import.\n"
        + ImportLine + "\n";

    internal static string FragmentPath(string slug) => $"{SitesDirectory}/{slug}{FragmentExtension}";

    /// <summary>Whether the box's base Caddyfile already imports the per-app files.</summary>
    internal static bool IsFragmentLayout(string? baseCaddyfile) =>
        baseCaddyfile is not null
        && baseCaddyfile.Split('\n').Any(line => string.Equals(line.Trim(), ImportLine, StringComparison.Ordinal));

    /// <summary>The apps that have a file in the sites directory, read from an <c>ls -1</c> of it.</summary>
    internal static IReadOnlyList<string> AppsWithFragments(string listing) =>
    [
        .. listing.Split('\n')
            .Select(line => line.Trim())
            .Where(name => name.Length > FragmentExtension.Length && name.EndsWith(FragmentExtension, StringComparison.Ordinal))
            .Select(name => name[..^FragmentExtension.Length]),
    ];

    /// <summary>A name beside <paramref name="path"/> to copy into first, so the move onto it is atomic and a half-written file is never imported.</summary>
    internal static string StagingPath(string path) => $"{path}.tmp-{Guid.NewGuid():N}";
}
