using System.Diagnostics;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>
///     Rask's runtime behaviour, for a parity page. A parity page is static markup: the kit's sheet and nothing else.
///     What Flux does in script the kit asks the RUNTIME for, by attribute (data-rask-tooltip, data-rask-dismiss-after,
///     data-rask-dismiss-scope — docs/js-interop-runtime.md), so a page that is walked with pointer and keyboard needs
///     those modules, exactly as an app has them. They are bundled here from their sources with the esbuild the build
///     already caches (the native binary ResolveTypeScriptToolTask fetched), and added to the page as one script: no
///     transport, no session — only the delegated listeners.
/// </summary>
public static class Runtime
{
    private static readonly string Resources = Repo.Path("src", "Rask.Core", "Resources");

    /// <summary>The modules — file names under src/Rask.Core/Resources, imported for their side effects — as one IIFE.</summary>
    public static string Bundle(params string[] modules)
    {
        var entry = string.Join('\n', modules.Select(name => $"import {Js.Quote("./" + name)};"));
        var start = new ProcessStartInfo(Esbuild())
        {
            WorkingDirectory = Resources,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "--bundle", "--format=iife", "--target=es2019", "--log-level=warning", "--loader=ts" })
        {
            start.ArgumentList.Add(arg);
        }

        using var built = Process.Start(start)!;
        built.StandardInput.Write(entry);
        built.StandardInput.Close();
        var error = built.StandardError.ReadToEndAsync();
        var output = built.StandardOutput.ReadToEnd();
        built.WaitForExit();
        if (built.ExitCode != 0)
        {
            throw new FluxExit(1, $"flux: bundling {string.Join(", ", modules)} failed\n{error.Result}");
        }

        return output;
    }

    /// <summary>Adds them to a loaded page.</summary>
    public static async Task With(IPage page, params string[] modules) =>
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = Bundle(modules) });

    // RASK_FLUX_ESBUILD names the binary; otherwise the newest one the build has cached (Rask.Core.targets).
    private static string Esbuild()
    {
        if (Environment.GetEnvironmentVariable("RASK_FLUX_ESBUILD") is { Length: > 0 } named)
        {
            return named;
        }

        var cacheRoot = Environment.GetEnvironmentVariable("RaskTypeScriptCacheRoot") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rask", "typescript");
        var cache = Path.Combine(cacheRoot, "esbuild");
        var binary = OperatingSystem.IsWindows() ? "esbuild.exe" : "esbuild";
        var found = Directory.Exists(cache)
            ? Directory.GetDirectories(cache).OrderByDescending(version => Path.GetFileName(version), StringComparer.Ordinal)
                .SelectMany(version => Directory.GetDirectories(version).Select(platform => Path.Combine(platform, "bin", binary)))
                .FirstOrDefault(File.Exists)
            : null;
        return found ?? throw new FluxExit(1, "flux: no esbuild — build src/Rask.Server once (it caches one), or set RASK_FLUX_ESBUILD.");
    }
}
