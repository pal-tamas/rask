using System.Globalization;

namespace Rask.Site.Features;

/// <summary>
///     When each guide's source last changed, as git recorded it when the site was built.
/// </summary>
/// <remarks>
///     <para>
///         One date, three readers: the "Updated" line a visitor sees on the guide, the article's
///         <c>dateModified</c> and <c>article:modified_time</c>, and — read back off that meta tag by the
///         prerender pass — the sitemap's <c>&lt;lastmod&gt;</c>. A crawler uses <c>lastmod</c> to decide what
///         to re-fetch, but only while it keeps turning out to be true; a site that stamps the build date on
///         every URL teaches it to ignore the field. So the date is git's, per file, or there is none.
///     </para>
///     <para>
///         <b>Embedded at build, not looked up at render.</b> The same page renders twice — once at publish
///         and again in the browser when the bundle takes over — and a crawler that runs JavaScript indexes
///         the second. A date only the publish could see would be rendered into the page and then removed
///         from it by the first live frame.
///     </para>
///     <para>
///         The resource is the raw output of one <c>git log --format=@%cs --name-only -- docs</c>, written by
///         the <c>RaskSiteGuideHistory</c> target in the csproj. Reducing it to one line per guide happens
///         here rather than in MSBuild, which has no way to pair a date line with the path lines under it.
///         A shallow clone or a build with no git writes an empty file, and every guide simply has no date.
///     </para>
/// </remarks>
public static class GuideHistory
{
    internal const string ResourceName = "raskdoc-history.txt";

    private static readonly Lazy<IReadOnlyDictionary<string, DateOnly>> Dates = new(Load);

    /// <summary>The date the guide's source last changed, or <c>null</c> when the build could not say.</summary>
    public static DateOnly? LastModified(string slug) =>
        Dates.Value.TryGetValue(slug, out var date) ? date : null;

    /// <summary>
    ///     Reads a <c>git log --format=@%cs --name-only</c> listing into the newest date per guide slug.
    /// </summary>
    /// <remarks>
    ///     The newest date rather than the first one seen. Log order is traversal order, and a branch merged
    ///     late carries commits dated before ones already listed — so "first" is usually right and
    ///     occasionally a week stale, which is the kind of wrong nobody ever notices.
    /// </remarks>
    internal static IReadOnlyDictionary<string, DateOnly> Parse(string log)
    {
        var dates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        DateOnly? current = null;

        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('@'))
            {
                current = DateOnly.TryParseExact(
                    line[1..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;
                continue;
            }

            if (current is not { } date
                || !line.StartsWith("docs/", StringComparison.Ordinal)
                || !line.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            // The bare leaf, because that is the slug: docs/apis/geolocation.md is /docs/guides/geolocation.
            var slug = Path.GetFileNameWithoutExtension(line);
            if (!dates.TryGetValue(slug, out var existing) || date > existing)
            {
                dates[slug] = date;
            }
        }

        return dates;
    }

    private static IReadOnlyDictionary<string, DateOnly> Load()
    {
        using var stream = typeof(GuideHistory).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
