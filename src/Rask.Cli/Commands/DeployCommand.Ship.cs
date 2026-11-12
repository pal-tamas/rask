using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// Where a deploy's image is built, and how it reaches the box. Built <em>here</em> — on the developer's machine or
/// the CI runner — for the box's platform, then streamed to it (<c>docker save | gzip | docker -H ssh://box load</c>):
/// a small VPS spends no memory or CPU on builds, so two apps deploying at once no longer compete for it, and the
/// build cache lives where the build runs. Only when this machine has no Linux Docker engine to build with, or the
/// box's platform can't be read, is the image built on the box as before.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>The release this deploy ships (<see cref="ReleaseName"/>), set when it starts; <c>null</c> outside one.</summary>
    private string? _release;

    /// <summary>
    /// Build the image and put it on the box as <c>:current</c>, the live one moved aside to <c>:previous</c> first.
    /// <c>false</c> (said why) when it could not; the box then still serves what it served.
    /// </summary>
    private async Task<bool> BuildAndLandImageAsync(DeployPlan plan, CancellationToken cancellationToken)
    {
        var (host, slug) = (plan.Host, plan.Slug);
        var platform = await PlatformToBuildHereAsync(host, cancellationToken).ConfigureAwait(false);
        if (platform is null)
        {
            // Move the live image aside before the build takes its tag, so the version being replaced stays
            // recoverable by `rask deploy rollback`. It fails harmlessly on a first deploy (no :current yet).
            await Run(BuildRetagArguments(host, slug, CurrentTag, PreviousTag), cancellationToken).ConfigureAwait(false); // ignore-absent
            WriteHeading($"Building {slug}:{CurrentTag} on {host}…");
            WriteRelease();
            return await BuildImageAsync(plan, BuildBuildArguments(host, slug, plan.Dockerfile, plan.ContextDir, _release), cancellationToken).ConfigureAwait(false);
        }

        WriteHeading($"Building {slug}:{CurrentTag} here for {platform}…");
        WriteRelease();
        if (!await BuildImageAsync(plan, BuildLocalBuildArguments(slug, platform, plan.Dockerfile, plan.ContextDir, _release), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Only now, with an image to send, does anything on the box move.
        await Run(BuildRetagArguments(host, slug, CurrentTag, PreviousTag), cancellationToken).ConfigureAwait(false); // ignore-absent
        return await ShipImageAsync(host, slug, cancellationToken).ConfigureAwait(false);
    }

    private void WriteRelease()
    {
        if (_release is not null)
        {
            Console.WriteLine($"  Release {_release} — what /version answers (Rask:Version).", ConsoleStyle.Dim);
        }
    }

    /// <summary>
    /// The box's platform (<c>linux/amd64</c>) when the image can be built here for it, or <c>null</c> to build on the
    /// box: this machine has no Linux Docker engine (only the CLI, or Windows containers), or the box's platform can't
    /// be read. The two are asked together. A build for another architecture than this machine's — an arm64 Mac
    /// deploying to an amd64 VPS — runs under emulation, slower, but still off the box.
    /// </summary>
    private async Task<string?> PlatformToBuildHereAsync(string host, CancellationToken cancellationToken)
    {
        var here = Capture(BuildPlatformArguments(host: null), cancellationToken);
        var there = Capture(BuildPlatformArguments(host), cancellationToken);
        await Task.WhenAll(here, there).ConfigureAwait(false);

        if (LinuxPlatform(await there.ConfigureAwait(false)) is not { } box)
        {
            Console.WriteLine($"  Could not read {HostName(host)}'s platform — building on it.", ConsoleStyle.Dim);
            return null;
        }

        if (LinuxPlatform(await here.ConfigureAwait(false)) is null)
        {
            Console.WriteLine($"  No Linux Docker engine answers on this machine — building on {HostName(host)}, which takes its memory while it runs. Start Docker here to build off the box.", ConsoleStyle.Dim);
            return null;
        }

        return box;
    }

    /// <summary><c>linux/&lt;arch&gt;</c> from a <c>docker version</c> that answered one, else <c>null</c>.</summary>
    internal static string? LinuxPlatform(ProcessResult version)
    {
        var platform = version.ExitCode == 0 ? version.StandardOutput.Trim() : string.Empty;
        return platform.StartsWith("linux/", StringComparison.Ordinal)
            && platform.Length > "linux/".Length
            && platform["linux/".Length..].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? platform
            : null;
    }

    /// <summary>
    /// Stream the image built here to the box, gzip-compressed, and tag it <c>:latest</c> there too. The copy here is
    /// removed either way — the build cache keeps what the next build reuses — so a laptop that deploys every day does
    /// not fill up with images it only sent.
    /// </summary>
    private async Task<bool> ShipImageAsync(string host, string slug, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"Sending {slug}:{CurrentTag} to {HostName(host)}…").ConfigureAwait(false);
        var sent = await _process.PipeAsync("docker", BuildSaveArguments(slug), BuildLoadArguments(host), _workingDirectory, cancellationToken).ConfigureAwait(false);
        await Capture(BuildLocalImageRemoveArguments(slug), cancellationToken).ConfigureAwait(false);
        if (sent != 0)
        {
            Console.WriteErrorLine($"Sending the image to {HostName(host)} failed — the output above says why. The previous version keeps serving; deploy again.", ConsoleStyle.Error);
            return false;
        }

        // Kept so `docker images` on the box reads the way a person expects; nothing runs from it.
        await Run(BuildRetagArguments(host, slug, CurrentTag, "latest"), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The release a container started from <c>:<paramref name="tag"/></c> is told it is: this deploy's for the image it
    /// just built, else the one the image was built as (a rollback brings its name back with it), and <c>null</c> when
    /// the user named the release in their own <c>--env</c> — theirs then stands — or the image carries none.
    /// </summary>
    private async Task<string?> ReleaseForAsync(string host, string slug, string tag, IReadOnlyList<string> env, CancellationToken cancellationToken)
    {
        if (ReleaseName.Given(env) is not null)
        {
            return null;
        }

        if (string.Equals(tag, CurrentTag, StringComparison.Ordinal) && _release is not null)
        {
            return _release;
        }

        var label = await Capture(BuildReleaseLabelArguments(host, slug, tag), cancellationToken).ConfigureAwait(false);
        var name = label.ExitCode == 0 ? label.StandardOutput.Trim() : string.Empty;
        return name.Length == 0 || name.Equals("<no value>", StringComparison.Ordinal) ? null : name;
    }
}
