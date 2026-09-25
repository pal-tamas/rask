using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

/// <summary>
///     A guide's pictures: linked where they live in the repository, so GitHub renders them, and served by the site from
///     its own root, so the guide page does too — one file, no second copy to keep in step.
/// </summary>
public sealed partial class GuideImageTests
{
    [Fact]
    public void A_picture_the_site_serves_is_linked_from_the_site_on_the_guide_page()
    {
        var html = Markdown.RewriteImages(
            Markdig.Markdown.ToHtml("![The Wire tab](../src/Rask.Site/wwwroot/img/devtools/wire.webp)", Markdown.Pipeline),
            "devtools.md");

        Assert.Contains($"<img src=\"{LiveOptions.PathBase}/img/devtools/wire.webp\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"The Wire tab\"", html, StringComparison.Ordinal);
        // Below the guide's opening paragraphs, so it waits until the reader scrolls to it.
        Assert.Contains("loading=\"lazy\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_other_repository_picture_is_GitHubs_raw_file_not_its_page()
    {
        var html = Markdown.RewriteImages(
            Markdig.Markdown.ToHtml("![A chart](../tests/Bench/chart.png)", Markdown.Pipeline),
            "devtools.md");

        // A blob URL is an HTML page about the file, which an <img> cannot show.
        Assert.Contains("src=\"https://github.com/pal-tamas/rask/raw/main/tests/Bench/chart.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_picture_is_left_as_written()
    {
        var html = Markdown.RewriteImages(
            Markdig.Markdown.ToHtml("![badge](https://img.shields.io/x.svg)", Markdown.Pipeline),
            "devtools.md");

        Assert.Contains("src=\"https://img.shields.io/x.svg\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_twin_links_a_picture_the_site_serves_where_the_site_serves_it()
    {
        var twin = LlmsText.Twin("![The Wire tab](../src/Rask.Site/wwwroot/img/devtools/wire.webp)\n", "devtools.md");

        Assert.Contains("](https://rask.sh/img/devtools/wire.webp)", twin, StringComparison.Ordinal);
    }

    // The failure a relative picture invites: a file renamed or never committed renders as a broken image on GitHub and
    // on the site alike, and nothing else in the build reads an image path.
    [Fact]
    public void Every_picture_a_guide_shows_is_a_file_the_site_serves()
    {
        var root = RepositoryRoot();
        var docs = Path.Combine(root, "docs");
        var missing = new List<string>();
        var seen = 0;

        foreach (var file in Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories))
        {
            var sourcePath = Path.GetRelativePath(docs, file).Replace('\\', '/');
            foreach (Match match in ImageRegex().Matches(File.ReadAllText(file)))
            {
                var link = match.Groups["path"].Value;
                if (link.Contains("://", StringComparison.Ordinal))
                {
                    continue;
                }

                seen++;
                var target = DocLinks.Resolve(sourcePath, link);
                if (target.SitePath is null || !File.Exists(Path.Combine(root, target.RepositoryPath)))
                {
                    missing.Add($"{sourcePath} → {link}");
                }
            }
        }

        Assert.True(seen > 0, "no guide shows a picture, so this check would pass for nothing");
        Assert.True(
            missing.Count == 0,
            "These pictures are not files under src/Rask.Site/wwwroot, so the guide page cannot serve them:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
    }

    private static string RepositoryRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }

    // ![alt](path "title") — the path only.
    [GeneratedRegex("""!\[[^\]]*\]\((?<path>[^)\s]+)(?:\s+"[^"]*")?\)""")]
    private static partial Regex ImageRegex();
}
