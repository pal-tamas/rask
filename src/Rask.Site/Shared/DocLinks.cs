using Rask.Site.Features;

namespace Rask.Site;

/// <summary>
///     Where a relative link in a doc under <c>docs/</c> actually points — resolved once, for both places the
///     docs are published: the guide pages and their Markdown twins.
/// </summary>
/// <remarks>
///     <para>
///         The docs are written to be read on GitHub, so their links are relative to the file they sit in:
///         <c>routing.md</c>, <c>../browser-capabilities.md</c> from <c>docs/apis/</c>,
///         <c>../tests/Rask.Cqrs.Tests</c>. Served from <c>/docs/guides/{slug}/</c>, none of those resolve. A
///         link that lands on a guide becomes that guide; anything else becomes the file or folder on GitHub,
///         which is where it lives.
///     </para>
///     <para>
///         <b>Resolved against the linking doc's own folder.</b> The page renderer used to read every <c>../</c>
///         as "the repository root" and keep only the file name — so 51 <c>../browser-capabilities.md</c> links
///         became GitHub 404s, <c>../tests/Rask.Benchmarks.Sqlite/Baselines/README.md</c> became the
///         repository's own README, and a link to anything that was not Markdown was left relative and 404ed
///         on the site.
///     </para>
/// </remarks>
public static class DocLinks
{
    /// <summary>What a relative doc link resolves to.</summary>
    /// <param name="GuideSlug">The guide it lands on, or <c>null</c> when it lands on another repository file.</param>
    /// <param name="RepositoryPath">The target's path from the repository root, e.g. <c>docs/cqrs.md</c>.</param>
    public readonly record struct Target(string? GuideSlug, string RepositoryPath)
    {
        /// <summary>The target on GitHub. A blob URL naming a folder redirects to the folder's tree view.</summary>
        public string GitHubUrl => $"{SiteIdentity.Repository}/blob/main/{RepositoryPath}";
    }

    /// <summary>Resolves <paramref name="link" />, as written in the doc at <paramref name="sourcePath" />.</summary>
    /// <param name="sourcePath">
    ///     The linking doc's path under <c>docs/</c>, e.g. <c>apis/geolocation.md</c>; <c>null</c> reads as a
    ///     doc at the top of <c>docs/</c>.
    /// </param>
    /// <param name="link">A relative link without its fragment: <c>../cli.md</c>, <c>../tests/Rask.Cqrs.Tests</c>.</param>
    public static Target Resolve(string? sourcePath, string link)
    {
        var segments = new List<string> { "docs" };
        var folder = sourcePath is null ? null : Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder))
        {
            segments.AddRange(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var segment in link.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // Clamped at the repository root, as a URL is: there is nothing above it to name.
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        var path = string.Join('/', segments);

        // A guide only when the link lands INSIDE docs/ on a Markdown file the catalog names. A file elsewhere
        // that shares a guide's name — a benchmark's own sqlite.md — is that file, not the guide.
        if (segments.Count > 1
            && segments[0] == "docs"
            && path.EndsWith(".md", StringComparison.Ordinal)
            && GuideCatalog.Find(Path.GetFileNameWithoutExtension(path)) is { } guide)
        {
            return new Target(guide.Slug, path);
        }

        return new Target(null, path);
    }
}
