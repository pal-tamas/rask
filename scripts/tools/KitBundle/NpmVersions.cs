using Semver;

namespace KitBundle;

/// <summary>Which release of a package a dependency spec installs, chosen as npm chooses it.</summary>
/// <remarks>
///     npm's own rule (npm-pick-manifest): a dist-tag names its release; for a range, the <c>latest</c> tag wins
///     when the range accepts it, and otherwise the newest release the range accepts — a deprecated one only when
///     nothing else is. A spec npm reads as neither (a URL, a <c>git:</c> or <c>npm:</c> alias) is refused: an
///     engine that bundles one is the owner's call, not this tool's.
/// </remarks>
internal static class NpmVersions
{
    internal static NpmRelease Pick(NpmPackument packument, string spec)
    {
        var byVersion = packument.Releases.ToDictionary(r => r.Version.ToString(), StringComparer.Ordinal);
        if (packument.Tags.TryGetValue(spec, out var tagged) && byVersion.TryGetValue(tagged, out var tag))
        {
            return tag;
        }

        if (!SemVersionRange.TryParseNpm(spec, out var range))
        {
            throw new KitBundleException($"{packument.Name}@{spec} is not a version, a range or a dist-tag");
        }

        if (packument.Tags.TryGetValue("latest", out var latestVersion)
            && byVersion.TryGetValue(latestVersion, out var latest)
            && range.Contains(latest.Version))
        {
            return latest;
        }

        return packument.Releases
                   .Where(r => range.Contains(r.Version))
                   .OrderBy(r => r.Deprecated)
                   .ThenByDescending(r => r.Version, SemVersion.PrecedenceComparer)
                   .FirstOrDefault()
               ?? throw new KitBundleException($"no release of {packument.Name} satisfies {spec}");
    }

    /// <summary>Whether a release already in the tree serves <paramref name="spec" />.</summary>
    internal static bool Satisfies(SemVersion version, string spec) =>
        SemVersionRange.TryParseNpm(spec, out var range) ? range.Contains(version) : string.Equals(version.ToString(), spec, StringComparison.Ordinal);
}
