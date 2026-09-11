using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;

namespace Rask.Site.Features;

/// <summary>
///     The docs as plain Markdown, for AI assistants: <c>/llms.txt</c>, <c>/llms-full.txt</c>, and a <c>.md</c>
///     twin beside every guide.
/// </summary>
/// <remarks>
///     <para>
///         An assistant asked about Rask reads the site the way a crawler does, and a guide page is a quarter
///         of a megabyte of navigation, highlighting spans and boot script around a few kilobytes of prose.
///         <see href="https://llmstxt.org">llms.txt</see> is the convention for handing it the prose instead:
///         an index at the root that names each document with a one-line note, the Markdown of each document
///         at its page's address plus <c>.md</c>, and the whole set in one file for a tool that wants it all
///         at once.
///     </para>
///     <para>
///         <b>Built from the catalog and the embedded docs the pages render</b>, so a guide added to the site
///         is in all three files by the same one edit, and its note in the index is its search description —
///         the sentence already written, reviewed and length-checked to say what the page is.
///     </para>
///     <para>
///         Written by the prerender publish (see <c>Program.cs</c>), the one run that has a publish directory.
///         The repo-root <c>llms.txt</c> is a different file for a different reader — an assistant working
///         inside a checkout, where relative links resolve — and it points here for the published set.
///     </para>
/// </remarks>
public static partial class LlmsText
{
    /// <summary>The index, at the site root.</summary>
    public const string IndexFileName = "llms.txt";

    /// <summary>Every guide in one file, at the site root.</summary>
    public const string FullFileName = "llms-full.txt";

    // The spec's "Optional" section: what a tool short on context may skip. The contributor notes describe
    // how Rask itself is built and released — exactly right for someone changing Rask, and noise for the
    // far larger number of people asking how to build an app with it. They stay out of llms-full.txt too.
    private const string OptionalGroup = "Contributing & internals";

    private const string Overview =
        "Rask is a C# component framework for ASP.NET Core and WebAssembly. Pages and components are plain "
        + "C# classes written as a markup chain (`Div.Class(\"panel\")[Span[\"hi\"]]`), server-rendered and kept "
        + "live over a WebSocket, or run in the browser on WebAssembly. Around the UI it ships the rest of a "
        + "product: the `rask` CLI (new, dev, db, deploy), EF Core data, CQRS, authentication, and background "
        + "jobs, email, cache and an outbox on the app's own SQLite database. Install the CLI with "
        + "`curl -sSL https://rask.sh/rask.sh | sh`.";

    private static string Root => PageMeta.Origin + LiveOptions.PathBase;

    /// <summary>The guide's Markdown twin, as a path from the site root: <c>/docs/guides/cqrs.md</c>.</summary>
    /// <remarks>
    ///     Beside the page's directory rather than inside it, so the address is the page's own with
    ///     <c>.md</c> on the end — the form the convention names, and the one a person guesses.
    /// </remarks>
    public static string MarkdownPath(string slug) => $"{Routes.GuidePage(slug)}.md";

    /// <summary>The absolute URL of the guide's Markdown twin.</summary>
    public static string MarkdownUrl(string slug) => Root + MarkdownPath(slug);

    /// <summary>The absolute URL of the guide's HTML page — its canonical.</summary>
    public static string PageUrl(string slug) => Root + PageMeta.CanonicalPath(Routes.GuidePage(slug));

    /// <summary>The text of <c>/llms.txt</c>.</summary>
    public static string Index()
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(SiteIdentity.Name).Append("\n\n");
        sb.Append("> ").Append(SiteIdentity.Description).Append("\n\n");
        sb.Append(Overview).Append("\n\n");
        sb.Append("Each link below is the Markdown source of one guide; the same guide as a web page is at that ")
            .Append("address without `.md`, ending in a slash. Every guide except the Optional ones, in one file: ")
            .Append(Root).Append('/').Append(FullFileName).Append('\n');

        foreach (var group in GuideChrome.ReadingOrder().GroupBy(guide => guide.Group))
        {
            sb.Append("\n## ").Append(group.Key == OptionalGroup ? "Optional" : group.Key).Append("\n\n");
            foreach (var guide in group)
            {
                sb.Append("- [").Append(guide.Title).Append("](").Append(MarkdownUrl(guide.Slug)).Append("): ")
                    .Append(guide.Description).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>The text of <c>/llms-full.txt</c>: every guide but the optional ones, in reading order.</summary>
    public static string Full()
    {
        var guides = GuideChrome.ReadingOrder()
            .Where(guide => guide.Group != OptionalGroup)
            .Select(guide => (Guide: guide, Markdown: GuideCatalog.ReadMarkdown(guide.Slug)))
            .Where(pair => pair.Markdown is not null)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("# ").Append(SiteIdentity.Name).Append(" — every guide in one file\n\n");
        sb.Append("> ").Append(SiteIdentity.Description).Append("\n\n");
        sb.Append(guides.Count).Append(" guides, in reading order. Each starts with a `Source:` line naming its ")
            .Append("page; the index is ").Append(Root).Append('/').Append(IndexFileName).Append('\n');

        foreach (var (guide, markdown) in guides)
        {
            sb.Append("\n---\n\nSource: ").Append(PageUrl(guide.Slug)).Append("\n\n");
            sb.Append(Twin(markdown!, GuideCatalog.SourcePath(guide.Slug)).Trim()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    ///     A guide's Markdown as it is published: links that resolve off the repository, and no demo markers.
    /// </summary>
    /// <param name="markdown">The doc's source.</param>
    /// <param name="sourcePath">The doc's path under <c>docs/</c>, which its relative links are relative to.</param>
    /// <remarks>
    ///     <para>
    ///         Every relative link goes through <see cref="DocLinks" />, the same resolver the guide pages use,
    ///         so a page and its twin cannot send a reader to two different places.
    ///     </para>
    ///     <para>
    ///         A code fence and an inline code span are left alone: they show text, and "correcting"
    ///         <c>handlers[0](context)</c> into a GitHub URL changes the example.
    ///     </para>
    /// </remarks>
    public static string Twin(string markdown, string sourcePath)
    {
        var sb = new StringBuilder(markdown.Length);
        var fenced = false;

        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                sb.Append(line).Append('\n');
                continue;
            }

            if (fenced)
            {
                sb.Append(line).Append('\n');
                continue;
            }

            // A demo marker mounts a live component on the site. In Markdown it is an HTML comment naming a
            // registry key, which tells a reader nothing — drop the line rather than ship the key.
            if (DemoMarker().IsMatch(line) && DemoMarker().Replace(line, "").Trim().Length == 0)
            {
                continue;
            }

            sb.Append(RewriteLine(line, sourcePath)).Append('\n');
        }

        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>
    ///     Writes the index, the full file and every guide's twin under <paramref name="root" />.
    /// </summary>
    /// <returns>How many guide twins were written.</returns>
    public static int WriteAll(string root)
    {
        File.WriteAllText(Path.Combine(root, IndexFileName), Index());
        File.WriteAllText(Path.Combine(root, FullFileName), Full());

        var written = 0;
        foreach (var guide in GuideCatalog.All)
        {
            if (GuideCatalog.ReadMarkdown(guide.Slug) is not { } markdown)
            {
                continue;
            }

            var file = Path.Combine(root, Path.Combine(MarkdownPath(guide.Slug).TrimStart('/').Split('/')));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Twin(markdown, GuideCatalog.SourcePath(guide.Slug)));
            written++;
        }

        // Said out loud, like the prerender pass's own counts: a missing llms.txt is invisible in a browser.
        Console.WriteLine(
            $"[Rask.Site] wrote {IndexFileName}, {FullFileName} and {written} Markdown guide(s) to {root}");
        return written;
    }

    // The line with its links rewritten everywhere but inside `code spans`: splitting on the backtick leaves
    // prose at the even indexes and code at the odd ones. A reference definition can only open the line.
    private static string RewriteLine(string line, string sourcePath)
    {
        var parts = line.Split('`');
        for (var i = 0; i < parts.Length; i += 2)
        {
            parts[i] = InlineLink().Replace(parts[i], m =>
                $"]({Url(sourcePath, m.Groups["path"].Value)}{m.Groups["frag"].Value}{m.Groups["title"].Value})");

            // Site-rooted — "/docs/ui/actions". It resolves on rask.sh, but llms-full.txt is read out of context,
            // where a path with no host resolves against nothing.
            parts[i] = RootedLink().Replace(parts[i], m => $"]({Root}{m.Groups["path"].Value})");
        }

        parts[0] = ReferenceLink().Replace(parts[0], m =>
            $"{m.Groups["lead"].Value}{Url(sourcePath, m.Groups["path"].Value)}{m.Groups["frag"].Value}");

        return string.Join('`', parts);
    }

    private static string Url(string sourcePath, string link)
    {
        var target = DocLinks.Resolve(sourcePath, link);
        return target.GuideSlug is { } slug ? PageUrl(slug) : target.GitHubUrl;
    }

    // ](/path) — rooted on the site, but not protocol-relative (//host).
    [GeneratedRegex(@"\]\((?<path>/(?!/)[^)\s]*)\)")]
    private static partial Regex RootedLink();

    [GeneratedRegex(@"<!--\s*demo:\s*[a-z0-9][a-z0-9-]*\s*-->")]
    private static partial Regex DemoMarker();

    // ](path#frag "title") — relative only: not a scheme, not rooted, not a bare fragment. Any target, not just
    // Markdown: ../tests/Rask.Cqrs.Tests is as dead on the site as ../cli.md was.
    [GeneratedRegex("""\]\((?<path>(?![a-zA-Z][a-zA-Z0-9+.-]*:|/|#)[^)\s#]+)(?<frag>#[^)\s]*)?(?<title>\s+"[^"]*")?\)""")]
    private static partial Regex InlineLink();

    // [label]: path#frag — a reference-style link definition.
    [GeneratedRegex(@"^(?<lead>\s*\[[^\]]+\]:\s*)(?<path>(?![a-zA-Z][a-zA-Z0-9+.-]*:|/|#)[^\s#]+)(?<frag>#\S*)?")]
    private static partial Regex ReferenceLink();
}
