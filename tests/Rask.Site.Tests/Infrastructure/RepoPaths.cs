namespace Rask.Site.Tests.Infrastructure;

internal static class RepoPaths
{
    /// <summary>The repository root, found by walking up from the test binaries to <c>Rask.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The site's own <c>wwwroot</c>, the files a published rask.sh serves.</summary>
    public static string SiteWebRoot => Path.Combine(Root, "src", "Rask.Site", "wwwroot");

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
