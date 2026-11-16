using System.Globalization;
using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// A deploy from GitHub Actions keeps its build cache between runs (<see cref="RunnerBuildCache"/>): the second deploy
/// downloads only what changed. Anything that goes wrong here costs speed, never the deploy — the build is then the
/// plain one, said why in one line.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>Reads one environment variable: the process's own in the CLI, none at all in a test.</summary>
    internal Func<string, string?> ReadEnvironment { get; set; } = static _ => null;

    /// <summary>
    /// The runner cache this build reads and writes, its builder started and its cache mounts filled; <c>null</c> for
    /// the plain build — not a GitHub Actions run, or one whose runner cannot start a BuildKit container.
    /// </summary>
    private async Task<RunnerBuildCache?> RunnerBuildCacheAsync(DeployPlan plan, CancellationToken cancellationToken)
    {
        var dockerfile = _fileSystem.FileExists(plan.Dockerfile) ? _fileSystem.ReadAllText(plan.Dockerfile) : string.Empty;
        if (RunnerBuildCache.For(ReadEnvironment, plan.Slug, dockerfile) is not { } cache)
        {
            return null;
        }

        if ((await Capture(BuildRunnerBuilderInspectArguments(), cancellationToken).ConfigureAwait(false)).ExitCode != 0
            && await Capture(BuildRunnerBuilderCreateArguments(), cancellationToken).ConfigureAwait(false) is { ExitCode: not 0 } created)
        {
            Console.WriteLine($"  Could not start a BuildKit builder on this runner ({Reason(created)}) — building without the cache kept between deploys.", ConsoleStyle.Dim);
            return null;
        }

        // A folder left by a deploy that stopped half-way would be read as this build's.
        _fileSystem.TryDeleteDirectory(cache.NextLayers);
        _fileSystem.TryDeleteDirectory(cache.NextMounts);
        var restored = _fileSystem.DirectoryExists(cache.Layers);
        var fingerprint = FingerprintOf(plan, dockerfile);
        cache = cache with
        {
            Fingerprint = fingerprint,
            Unchanged = restored && fingerprint.Length > 0 && _fileSystem.FileExists(cache.FingerprintFile)
                && string.Equals(_fileSystem.ReadAllText(cache.FingerprintFile).Trim(), fingerprint, StringComparison.Ordinal),
        };
        Console.WriteLine(CacheState(restored, cache.Unchanged), ConsoleStyle.Dim);
        await FillCacheMountsAsync(cache, cancellationToken).ConfigureAwait(false);
        return cache;
    }

    private static string CacheState(bool restored, bool unchanged)
    {
        if (!restored)
        {
            return "  Build cache: none yet (a first deploy, or the workflow has no cache step) — this build fills it for the next one.";
        }

        return unchanged
            ? "  Build cache: the last deploy's, for these same project files — nothing to download, and nothing new to keep."
            : "  Build cache: the last deploy's, restored by the workflow — only what changed is downloaded.";
    }

    /// <summary>The build's <see cref="BuildInputs.Fingerprint"/>, or empty when its files cannot be read (the cache is then written).</summary>
    private string FingerprintOf(DeployPlan plan, string dockerfile)
    {
        try
        {
            return BuildInputs.Fingerprint(_fileSystem, plan.ContextDir, dockerfile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>Copy the saved cache-mount folders into the fresh builder, so the app's build finds its downloads there.</summary>
    private async Task FillCacheMountsAsync(RunnerBuildCache cache, CancellationToken cancellationToken)
    {
        if (cache.MountIds.Count == 0 || !_fileSystem.DirectoryExists(cache.Mounts))
        {
            return;
        }

        _fileSystem.CreateDirectory(cache.Transfer);
        _fileSystem.WriteAllText(Path.Combine(cache.Transfer, "inbound.Dockerfile"), CacheMountTransfer.Inbound(cache.MountIds));
        if (await Capture(BuildMountsInArguments(cache, NewNonce()), cancellationToken).ConfigureAwait(false) is { ExitCode: not 0 } filled)
        {
            Console.WriteLine($"  Could not restore the build's cache mounts ({Reason(filled)}) — they start empty, so this build downloads what they held.", ConsoleStyle.Dim);
        }
    }

    /// <summary>
    /// Leave this build's cache where the workflow's cache step saves it from: the layer cache it wrote, and its cache
    /// mounts copied out. A part that cannot be kept is said, and the deploy goes on.
    /// </summary>
    private async Task KeepRunnerBuildCacheAsync(RunnerBuildCache cache, CancellationToken cancellationToken)
    {
        if (cache.Unchanged)
        {
            return;
        }

        if (_fileSystem.DirectoryExists(cache.NextLayers) && !TryReplace(cache.NextLayers, cache.Layers))
        {
            return;
        }

        if (cache.MountIds.Count == 0)
        {
            KeepFingerprint(cache);
            return;
        }

        _fileSystem.CreateDirectory(cache.Transfer);
        _fileSystem.WriteAllText(Path.Combine(cache.Transfer, "outbound.Dockerfile"), CacheMountTransfer.Outbound(cache.MountIds));
        var copied = await Capture(BuildMountsOutArguments(cache, NewNonce()), cancellationToken).ConfigureAwait(false);
        if (copied.ExitCode != 0)
        {
            Console.WriteLine($"  Could not keep the build's cache mounts ({Reason(copied)}) — the next deploy downloads what they held.", ConsoleStyle.Dim);
            return;
        }

        if (TryReplace(cache.NextMounts, cache.Mounts))
        {
            KeepFingerprint(cache);
            Console.WriteLine($"  Kept the build cache for the next deploy: its layers and {string.Join(", ", cache.MountIds)}.", ConsoleStyle.Dim);
        }
    }

    /// <summary>Only once both halves are kept: a fingerprint beside a half-written cache would stop it ever being completed.</summary>
    private void KeepFingerprint(RunnerBuildCache cache)
    {
        if (cache.Fingerprint.Length > 0)
        {
            _fileSystem.WriteAllText(cache.FingerprintFile, cache.Fingerprint);
        }
    }

    private bool TryReplace(string source, string destination)
    {
        try
        {
            _fileSystem.ReplaceDirectory(source, destination);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  Could not keep the build cache ({e.Message}) — the next deploy builds without it.", ConsoleStyle.Dim);
            return false;
        }
    }

    /// <summary>A value no earlier copy was built with (<see cref="CacheMountTransfer.Nonce"/>).</summary>
    internal Func<string> NewNonce { get; set; } = static () => Guid.NewGuid().ToString("N");

    /// <summary>The last line docker said on failure, for the one line that says why.</summary>
    private static string Reason(ProcessResult result) =>
        (result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture);
}
