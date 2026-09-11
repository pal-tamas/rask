using System.Diagnostics;

namespace Rask.External.Tests;

/// <summary>
///     Runs real MSBuild against the island build assets, for the tests whose subject is how the targets are wired
///     rather than what one task does.
/// </summary>
internal static class IslandBuild
{
    /// <summary>Runs <paramref name="file" /> and returns its exit code with stdout and stderr together.</summary>
    /// <remarks>
    ///     Both pipes are drained CONCURRENTLY — the reads are started and only awaited after the process exits.
    ///     Awaiting stdout to completion first deadlocks whenever the child fills the stderr pipe buffer (~64 KB)
    ///     while the parent is still blocked on stdout: the child blocks writing, never exits, and stdout never
    ///     closes. `dotnet msbuild` on a cold agent — NuGet output, first-run messages, and a failing build — is
    ///     exactly the shape that produces that much stderr.
    /// </remarks>
    public static async Task<(int Exit, string Output)> Run(string file, string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout + await stderr);
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
