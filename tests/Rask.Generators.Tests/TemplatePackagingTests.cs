using System.Diagnostics;
using System.IO.Compression;

namespace Rask.Generators.Tests;

// Packs the Rask.Templates project and inspects the produced .nupkg. This is the slow template test
// (it shells out to `dotnet pack`), so it lives apart from the fast, metadata-only TemplateConfigTests.
//
// It guards a real regression: NuGet treats a PackagePath with no file extension as a *directory*, so
// the templates' extensionless `Dockerfile`s packed as content/<t>/Dockerfile/<t>/Dockerfile — a
// folder — which shipped a broken `Dockerfile` directory into every `--docker` scaffold. The pack
// target (src/Rask.Templates/Rask.Templates.csproj) special-cases extensionless files to fix it; this
// test fails if that handling is lost or a new extensionless template file reintroduces the mangling.
public class TemplatePackagingTests
{
    [Fact]
    public void Pack_PlacesDockerfiles_AsFileEntries_NotNestedFolders()
    {
        var csproj = Path.Combine(RepoRoot(), "src", "Rask.Templates", "Rask.Templates.csproj");
        var outDir = Path.Combine(Path.GetTempPath(), "rask-templates-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var (exitCode, output) = RunDotnet($"pack \"{csproj}\" -o \"{outDir}\" --nologo -v minimal");
            Assert.True(exitCode == 0, $"dotnet pack failed (exit {exitCode}):\n{output}");

            var nupkg = Directory.GetFiles(outDir, "Rask.Templates.*.nupkg").SingleOrDefault();
            Assert.True(nupkg is not null, $"no Rask.Templates nupkg was produced in {outDir}:\n{output}");

            using var zip = ZipFile.OpenRead(nupkg!);
            var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

            // Each Dockerfile must be a file entry at exactly content/<template>/Dockerfile.
            foreach (var template in new[] { "rask-server", "rask-wasm", "rask-wasm-hosted" })
            {
                Assert.Contains($"content/{template}/Dockerfile", entries);
            }

            // The regression itself: no Dockerfile may appear as a path *segment* (a directory) — that
            // is the content/<t>/Dockerfile/<t>/Dockerfile mangling the extensionless-file handling prevents.
            Assert.DoesNotContain(entries, e => e.Contains("/Dockerfile/", StringComparison.Ordinal));

            // Sanity: the sibling assets (these have extensions, so they never tripped the bug) still ship.
            Assert.Contains("content/rask-wasm/nginx.conf", entries);
            Assert.Contains("content/rask-server/.dockerignore", entries);
        }
        finally
        {
            TryDeleteDirectory(outDir);
        }
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start 'dotnet'");

        // Read both streams concurrently so a full stderr buffer can't deadlock a blocking stdout read.
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(milliseconds: 300_000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the timeout and the Kill — nothing to do.
            }

            return (-1, "dotnet pack timed out after 300s");
        }

        proc.WaitForExit(); // ensure the async stdout/stderr readers have drained
        return (proc.ExitCode, stdout.Result + stderr.Result);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp directory is harmless.
        }
    }

    // Walks up from the test assembly to the repo root (the directory holding Rask.slnx).
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate repo root (Rask.slnx) from " + AppContext.BaseDirectory);
    }
}
