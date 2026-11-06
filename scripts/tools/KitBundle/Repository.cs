namespace KitBundle;

/// <summary>Where things are in the Rask checkout this tool runs in.</summary>
internal static class Repository
{
    /// <summary>The checkout's root: the nearest directory up from here, or from the tool, that holds Rask.slnx.</summary>
    internal static string Root { get; } = Find(Environment.CurrentDirectory) ?? Find(AppContext.BaseDirectory)
        ?? throw new KitBundleException("run this inside a Rask checkout (no Rask.slnx above here)");

    /// <summary>Rask.Ui, whose engines this tool builds.</summary>
    internal static string Kit => Path.Combine(Root, "src", "Rask.Ui");

    /// <summary>Where the committed engines and their notices live.</summary>
    internal static string Resources => Path.Combine(Kit, "Resources");

    /// <summary>Scratch space under the tool's own obj/: cleaned with it, ignored by git, never a source of the kit's build.</summary>
    internal static string Work => Path.Combine(Root, "scripts", "tools", "KitBundle", "obj", "kit");

    private static string? Find(string start)
    {
        for (var at = new DirectoryInfo(start); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Rask.slnx")))
            {
                return at.FullName;
            }
        }

        return null;
    }
}
