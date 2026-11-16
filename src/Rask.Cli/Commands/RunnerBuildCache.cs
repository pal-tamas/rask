using System.Text.RegularExpressions;

namespace Rask.Cli.Commands;

/// <summary>
/// The build cache a GitHub Actions runner keeps between deploys. A hosted runner starts every job with an empty
/// Docker, so without it every deploy downloads the NuGet packages, the Tailwind CLI, tsgo and Rask.Pdf's Chrome shell
/// again, and runs every <c>apt-get</c> and <c>workload install</c> layer again. The deploy workflow restores
/// <see cref="Root"/> with <c>actions/cache</c> before <c>rask deploy</c> runs and saves it after; the build writes it.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, because BuildKit exports only one of them. Its layer cache (<c>--cache-to type=local,mode=max</c>)
/// covers every layer, the build stage's too, but never the contents of a <c>RUN --mount=type=cache</c> — and the
/// template Dockerfiles keep exactly the downloads there, so a local build reuses them across csproj edits. So the
/// cache mounts are carried across on their own: copied out of the builder after the build (<see cref="Mounts"/>, one
/// folder per mount id) and copied back into a fresh builder before the next one (<see cref="CacheMountTransfer"/>).
/// </para>
/// <para>
/// Both need a BuildKit that runs in a container of its own (the <c>docker-container</c> driver, <see cref="Builder"/>):
/// the Docker engine's built-in builder can neither export a cache nor be reached by a bind of the runner's files. Its
/// image is <c>--load</c>ed into the engine, so the rest of the deploy sends it to the box exactly as before.
/// </para>
/// </remarks>
internal sealed partial record RunnerBuildCache(string Directory, IReadOnlyList<string> MountIds)
{
    /// <summary>The BuildKit builder a runner deploy builds on, created once per job.</summary>
    internal const string Builder = "rask-deploy";

    /// <summary>
    /// Under the runner's temp folder, which GitHub empties after every job, so nothing is left on a self-hosted runner
    /// but what the workflow's cache step put there. The workflow names the same folder (<c>${{ runner.temp }}</c>).
    /// </summary>
    internal const string Root = "rask-build-cache";

    /// <summary>
    /// The <see cref="BuildInputs.Fingerprint"/> of the build the saved cache was written by. Unchanged, the restored
    /// cache already holds everything this build downloads, and the workflow will not save it again either (its key is
    /// made of the same files), so the build neither writes a layer cache nor copies its mounts out: that is the half
    /// minute a deploy would otherwise spend on a cache no one keeps.
    /// </summary>
    internal string FingerprintFile => Path.Combine(Directory, "fingerprint");

    /// <summary>This build's <see cref="BuildInputs.Fingerprint"/>; empty when it could not be read.</summary>
    internal string Fingerprint { get; init; } = string.Empty;

    /// <summary>The restored cache was written by a build with this one's fingerprint: there is nothing to write.</summary>
    internal bool Unchanged { get; init; }

    /// <summary>The layer cache the build reads.</summary>
    internal string Layers => Path.Combine(Directory, "layers");

    /// <summary>
    /// The layer cache the build writes: a fresh folder each time, which then replaces <see cref="Layers"/>. A local
    /// cache written over its own source keeps every blob it ever held, so it would only grow.
    /// </summary>
    internal string NextLayers => Path.Combine(Directory, "layers-next");

    /// <summary>The cache mounts' contents, one folder per mount id.</summary>
    internal string Mounts => Path.Combine(Directory, "mounts");

    /// <summary>The cache mounts as copied out of this build, which then replaces <see cref="Mounts"/>.</summary>
    internal string NextMounts => Path.Combine(Directory, "mounts-next");

    /// <summary>The two small Dockerfiles that carry the cache mounts in and out, and the context of the outbound one.</summary>
    internal string Transfer => Path.Combine(Directory, "transfer");

    /// <summary>
    /// The runner cache for <paramref name="slug"/>, or <c>null</c> when this is not a GitHub Actions run (with no runner
    /// temp folder to keep it in): the build is then the plain one it always was.
    /// </summary>
    /// <param name="environment">Reads one environment variable.</param>
    /// <param name="slug">The app, so two apps deployed from one repository never share (or evict) one cache.</param>
    /// <param name="dockerfile">The Dockerfile's text, read for the cache mounts to carry.</param>
    internal static RunnerBuildCache? For(Func<string, string?> environment, string slug, string dockerfile)
    {
        if (!string.Equals(environment("GITHUB_ACTIONS"), "true", StringComparison.Ordinal)
            || environment("RUNNER_TEMP") is not { Length: > 0 } temp)
        {
            return null;
        }

        return new RunnerBuildCache(Path.Combine(temp, Root, slug), CacheMountIds(dockerfile));
    }

    /// <summary>
    /// The id of every <c>RUN --mount=type=cache</c> in <paramref name="dockerfile"/>, in order, each once. A mount
    /// without an id is keyed by its target, as BuildKit keys it. Only an id that is safe as a folder name and in a
    /// shell word is carried: one that is not (a target path like <c>/root/.nuget/packages</c>) is left to start empty.
    /// </summary>
    internal static IReadOnlyList<string> CacheMountIds(string dockerfile)
    {
        var ids = new List<string>();
        foreach (Match mount in MountOption().Matches(dockerfile))
        {
            var options = mount.Groups["options"].Value.Split(',')
                .Select(option => option.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);

            if (!options.TryGetValue("type", out var type) || !string.Equals(type, "cache", StringComparison.Ordinal))
            {
                continue;
            }

            var id = options.GetValueOrDefault("id") ?? options.GetValueOrDefault("target");
            if (id is not null && SafeId().IsMatch(id) && !ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    [GeneratedRegex(@"--mount=(?<options>\S+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MountOption();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeId();
}
