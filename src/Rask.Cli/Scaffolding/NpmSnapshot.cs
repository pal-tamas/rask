using System.Text.Json;
using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     The npm releases this CLI shipped with (<c>npm.snapshot.json</c>): what <c>rask new</c> chooses from when the
///     registry does not answer.
/// </summary>
/// <remarks>
///     Every package a template floats, in the registry's own document shape, so <see cref="NpmRegistry.Installable" />
///     and <see cref="NpmRegistry.BundledVite" /> read it as they read the registry. Refreshed every day by
///     <c>scripts/upstream/front-ends.py</c>, and proven to install by the template gates that scaffold through it.
/// </remarks>
internal static class NpmSnapshot
{
    private const string Resource = "RaskNpmSnapshot";

    private static readonly Lazy<(DateOnly Followed, NpmRegistry Registry)> Loaded = new(Load);

    /// <summary>The day the snapshot was last refreshed.</summary>
    internal static DateOnly Followed => Loaded.Value.Followed;

    /// <summary>A registry that answers from the snapshot alone, and never reaches the network.</summary>
    internal static NpmRegistry Registry => Loaded.Value.Registry;

    /// <summary>A registry over a snapshot document: <c>{ "followed": …, "packages": { name: { "versions": … } } }</c>.</summary>
    internal static (DateOnly Followed, NpmRegistry Registry) Read(JsonElement snapshot)
    {
        var packages = snapshot.GetProperty("packages");
        var releases = new Dictionary<string, IReadOnlyList<NpmRelease>>(StringComparer.Ordinal);
        var bundled = new Dictionary<SemVersion, SemVersion>();
        foreach (var package in packages.EnumerateObject())
        {
            releases[package.Name] = NpmRegistry.Installable(package.Value) ?? [];
        }

        if (packages.TryGetProperty(VitePlusToolchain.Package, out var toolchain))
        {
            foreach (var release in toolchain.GetProperty("versions").EnumerateObject())
            {
                if (SemVersion.TryParse(release.Name, SemVersionStyles.Strict, out var version)
                    && NpmRegistry.BundledVite(release.Value) is { } vite)
                {
                    bundled[version] = vite;
                }
            }
        }

        var registry = new NpmRegistry(
            (package, _) => Task.FromResult(releases.GetValueOrDefault(package)),
            (version, _) => Task.FromResult(bundled.GetValueOrDefault(version)));
        return (DateOnly.Parse(snapshot.GetProperty("followed").GetString()!, System.Globalization.CultureInfo.InvariantCulture), registry);
    }

    private static (DateOnly, NpmRegistry) Load()
    {
        using var stream = typeof(NpmSnapshot).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The {Resource} resource is missing from {typeof(NpmSnapshot).Assembly.GetName().Name}.");
        using var document = JsonDocument.Parse(stream);
        return Read(document.RootElement);
    }
}
