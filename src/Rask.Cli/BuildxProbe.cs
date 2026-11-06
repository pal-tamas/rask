using System.ComponentModel;

namespace Rask.Cli;

/// <summary>
///     Can the local docker CLI build a scaffolded Dockerfile? Every template's Dockerfile keeps NuGet, the Tailwind
///     CLI and the PDF shell in <c>RUN --mount=type=cache</c> steps, and only BuildKit understands them.
/// </summary>
/// <remarks>
///     <para>
///         A docker CLI without the buildx plugin sends <c>docker build</c> to the legacy builder, which stops
///         on the first <c>RUN --mount</c> — mid-deploy, after the build context has already gone up to the
///         host, in words that name neither BuildKit nor the missing plugin. Homebrew's <c>docker</c> formula
///         is exactly that CLI. Docker Desktop and get.docker.com ship buildx with it.
///     </para>
///     <para>
///         Podman's <c>docker</c> shim is fine: it builds with Buildah, which reads cache mounts natively, and
///         its <c>--version</c> says so ("podman version …"), so it is recognised before buildx is asked for.
///         The daemon <c>rask deploy</c> builds on is always a Docker Engine — its host setup installs one — so
///         the client is the only side left to check.
///     </para>
/// </remarks>
internal static class BuildxProbe
{
    /// <summary>The one line that says what is wrong, shared by <c>rask doctor</c> and <c>rask deploy</c>.</summary>
    public const string Problem =
        "this docker CLI has no buildx plugin, so `docker build` falls back to the legacy builder — "
        + "and the scaffolded Dockerfile's `RUN --mount=type=cache` steps fail there.";

    /// <summary>Where the prerequisite is written down.</summary>
    public const string DocsReference = "docs/deployment.md#scaffolding-a-dockerfile";

    /// <summary>Docker's own page for installing the plugin by hand.</summary>
    private const string DockerDocs = "https://docs.docker.com/go/buildx/";

    /// <summary>
    ///     Classifies the local docker CLI, given what its <c>docker --version</c> already printed.
    /// </summary>
    /// <param name="process">The runner the caller already holds.</param>
    /// <param name="dockerVersion">The output of <c>docker --version</c>, read for Podman's shim.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    public static async Task<BuildxStatus> CheckAsync(
        IProcessRunner process, string dockerVersion, CancellationToken cancellationToken)
    {
        if (IsPodman(dockerVersion))
        {
            return BuildxStatus.Podman;
        }

        try
        {
            var result = await process.CaptureAsync("docker", ["buildx", "version"], null, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? BuildxStatus.Present : BuildxStatus.Missing;
        }
        catch (Win32Exception)
        {
            // docker itself answered a moment ago; a launch failure now still means no build is possible.
            return BuildxStatus.Missing;
        }
    }

    /// <summary>True when <c>docker --version</c> came from Podman's shim rather than Docker's CLI.</summary>
    public static bool IsPodman(string dockerVersion) =>
        dockerVersion.Contains("podman", StringComparison.OrdinalIgnoreCase);

    /// <summary>The install command for this machine — a command to paste beats a page to go read.</summary>
    public static string FixHint() =>
        FixHint(OperatingSystem.IsMacOS(), OperatingSystem.IsLinux() && File.Exists("/etc/debian_version"));

    /// <summary>The install command for a machine described by its flags — testable from any host OS.</summary>
    /// <param name="macOS">The machine is a Mac, where Homebrew's <c>docker</c> is the one without buildx.</param>
    /// <param name="debian">The machine is Debian or Ubuntu, where Docker's apt repository packages the plugin.</param>
    public static string FixHint(bool macOS, bool debian)
    {
        if (macOS)
        {
            // Homebrew installs the plugin outside Docker's search path; its caveat prints the config line that adds it.
            return "Install it: brew install docker-buildx, then add the cliPluginsExtraDirs line its caveat "
                + $"prints to ~/.docker/config.json (or {DockerDocs})";
        }

        return debian
            ? $"Install it: sudo apt-get install docker-buildx-plugin (or {DockerDocs})"
            : $"Install the buildx plugin: {DockerDocs}";
    }
}
