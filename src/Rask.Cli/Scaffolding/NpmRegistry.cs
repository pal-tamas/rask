using System.Text.Json;
using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Asks the npm registry which releases of a package can be installed: stable, not deprecated, newest first.
/// </summary>
/// <remarks>
///     A <see langword="null" /> answer means the registry could not be asked — offline, filtered or too slow — and is
///     never a reason to stop a scaffold. Set on <c>NewCommand</c> by <c>CliApplication</c>; tests leave it unset or
///     pass their own lookup, so none of them reaches the network.
/// </remarks>
internal sealed class NpmRegistry(Func<string, CancellationToken, Task<IReadOnlyList<NpmRelease>?>> releasesOf)
{
    /// <summary>registry.npmjs.org, read through its abbreviated ("corgi") documents.</summary>
    internal static NpmRegistry Public { get; } = new(FetchFromNpmjsAsync);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal Task<IReadOnlyList<NpmRelease>?> ReleasesAsync(string package, CancellationToken cancellationToken) =>
        releasesOf(package, cancellationToken);

    private static async Task<IReadOnlyList<NpmRelease>?> FetchFromNpmjsAsync(string package, CancellationToken cancellationToken)
    {
        try
        {
            // The abbreviated document carries every version's peers, engines and deprecation, and nothing else.
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"https://registry.npmjs.org/{package.Replace("/", "%2F", StringComparison.Ordinal)}"));
            request.Headers.Accept.ParseAdd("application/vnd.npm.install-v1+json");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var disposeBody = body.ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Installable(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The stable, undeprecated releases in a registry document, newest first.</summary>
    internal static IReadOnlyList<NpmRelease>? Installable(JsonElement document)
    {
        if (!document.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var releases = new List<NpmRelease>();
        foreach (var entry in versions.EnumerateObject())
        {
            if (!SemVersion.TryParse(entry.Name, SemVersionStyles.Strict, out var version)
                || version.IsPrerelease
                || entry.Value.TryGetProperty("deprecated", out var deprecated) && deprecated.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            releases.Add(new NpmRelease(version, Strings(entry.Value, "peerDependencies"), Node(entry.Value)));
        }

        releases.Sort((a, b) => b.Version.ComparePrecedenceTo(a.Version));
        return releases;
    }

    private static Dictionary<string, string> Strings(JsonElement release, string property)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (release.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.String))
            {
                values[entry.Name] = entry.Value.GetString()!;
            }
        }

        return values;
    }

    private static string? Node(JsonElement release) =>
        release.TryGetProperty("engines", out var engines)
        && engines.ValueKind == JsonValueKind.Object
        && engines.TryGetProperty("node", out var node)
        && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
}
