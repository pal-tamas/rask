using Rask.TestFiles;

namespace Rask.External.Tests;

/// <summary>
///     Runs real MSBuild against the island build assets, for the tests whose subject is how the targets are wired
///     rather than what one task does.
/// </summary>
internal static class IslandBuild
{
    /// <summary>Runs <paramref name="file" /> and returns its exit code with stdout and stderr together.</summary>
    public static async Task<(int Exit, string Output)> Run(string file, string arguments, string workingDirectory)
    {
        var result = await TestProcess.Run(file, arguments, workingDirectory);
        return (result.ExitCode, result.Output);
    }

    /// <summary>The repository root, found by walking up from the test output to <c>Rask.slnx</c>.</summary>
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>Deletes a throwaway directory, tolerating a file something still holds open.</summary>
    public static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }
}
