using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Asks the package feed whether the Rask packages a scaffold pins actually exist at that version.
/// </summary>
/// <remarks>
///     <para>
///         <c>rask new</c> pins every Rask package at one version, and a package added after the last release has no
///         published version there: a scaffold carrying <c>Rask.Storage</c> or <c>Rask.DevTools</c> failed its restore
///         with a bare <c>NU1103</c> that named neither the cause nor a way out (#1083). Looking first means the command
///         can say which packages are missing, at which version, before the restore output buries it.
///     </para>
///     <para>
///         Never a reason to stop. An offline machine, a proxy or a slow feed answers nothing, and nothing is reported;
///         the restore that follows decides. Only a feed that answers — with a version list that lacks the pin, or a
///         404 for a package it has never heard of — produces a finding.
///     </para>
/// </remarks>
internal sealed partial class PackageFeed(Func<string, CancellationToken, Task<IReadOnlyCollection<string>?>> versionsOf)
{
    /// <summary>nuget.org's flat container: one small JSON document of versions per package id.</summary>
    internal static PackageFeed NuGetOrg { get; } = new(FetchFromNuGetOrgAsync);

    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The <c>Rask*</c> package references the scaffold's project files pin, with the version each pins.</summary>
    internal static IReadOnlyList<(string Id, string Version)> RaskReferences(IEnumerable<ScaffoldFile> files) =>
    [
        .. files
            .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => PackageReference().Matches(f.Content))
            .Select(m => (Id: m.Groups["id"].Value, Version: m.Groups["version"].Value))
            .Distinct(),
    ];

    /// <summary>The references whose pinned version the feed says does not exist. Empty when the feed could not be asked.</summary>
    internal async Task<IReadOnlyList<(string Id, string Version)>> FindUnpublishedAsync(
        IReadOnlyList<(string Id, string Version)> references, CancellationToken cancellationToken)
    {
        var lookups = references
            .Select(async reference => (reference, versions: await versionsOf(reference.Id, cancellationToken).ConfigureAwait(false)))
            .ToArray();

        var results = await Task.WhenAll(lookups).ConfigureAwait(false);
        return
        [
            .. results
                .Where(r => r.versions is not null && !r.versions.Contains(r.reference.Version, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.reference),
        ];
    }

    private static async Task<IReadOnlyCollection<string>?> FetchFromNuGetOrgAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(LookupTimeout);
            using var http = new HttpClient();
            using var response = await http
                .GetAsync($"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json", timeout.Token)
                .ConfigureAwait(false);

            // The feed has never heard of it: no version at all is published.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array
                ? [.. versions.EnumerateArray().Select(v => v.GetString()).OfType<string>()]
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Offline, filtered, or too slow: not knowing is not a finding.
            return null;
        }
    }

    [GeneratedRegex("""<PackageReference\s+Include="(?<id>Rask[A-Za-z0-9.]*)"\s+Version="(?<version>[^"]+)"\s*/?>""")]
    private static partial Regex PackageReference();
}
