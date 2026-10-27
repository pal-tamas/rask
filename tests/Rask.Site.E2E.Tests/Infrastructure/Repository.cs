namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>The checkout the tests run from: the directory that holds <c>Rask.slnx</c>, found walking up.</summary>
internal static class Repository
{
    /// <summary>The checkout's root; throws when the tests run from outside one.</summary>
    public static string Root => Find() ?? throw new InvalidOperationException(
        $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");

    /// <summary>The checkout's root, or null when the tests run from outside one.</summary>
    public static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
