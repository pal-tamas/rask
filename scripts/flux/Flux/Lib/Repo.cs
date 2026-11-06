namespace Rask.Flux;

/// <summary>Where things are in the repository the harness runs from.</summary>
public static class Repo
{
    private static readonly Lazy<string> Found = new(Find);

    /// <summary>
    ///     The repository's root: RASK_REPO_ROOT where it is set (a test runs a command in a copy of the files it writes),
    ///     else the folder holding <c>Rask.slnx</c> above this build or the current directory.
    /// </summary>
    public static string Root => Found.Value;

    /// <summary>A path under the root.</summary>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>artifacts/flux-parity, where every measurement, page and shot of the harness lives.</summary>
    public static string Parity(params string[] parts) => Path(["artifacts", "flux-parity", .. parts]);

    /// <summary>A file's <c>file://</c> URL, as Node's pathToFileURL writes it.</summary>
    public static string FileUrl(string path) => new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>Node's <c>process.platform</c>: what a lock taken on this machine is keyed on.</summary>
    public static string Platform => OperatingSystem.IsMacOS() ? "darwin" : OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsLinux() ? "linux" : "unknown";

    private static string Find()
    {
        if (Environment.GetEnvironmentVariable("RASK_REPO_ROOT") is { Length: > 0 } set)
        {
            return System.IO.Path.GetFullPath(set);
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "Rask.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new FluxExit(1, "flux: no Rask.slnx above the harness — run it from the repository.");
    }
}
