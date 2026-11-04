using System.Globalization;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Globalization;

// A site that ships its pages in several languages, published to a static host (#1199): every page written once
// per language — the default at the root, the others under their tag — each in its own language, with its <html
// lang>, a canonical to itself, hreflang alternates plus x-default, and the same alternates in the sitemap.
[Collection("RaskPrerenderEnvironment")]
public partial class LanguagePrefixPrerenderTests
{
    private const string RouteGroup = "LanguagePrefixPrerender";

    private const string Shell =
        """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"/><base href="/"/><title>Shell</title></head>
        <body data-rask-root><div class="rask-boot">Loading…</div><script src="main.js" type="module"></script></body>
        </html>
        """;

    [Fact]
    public async Task Every_page_is_written_once_per_language_each_in_its_own()
    {
        var dir = await PublishAsync();

        try
        {
            var hungarian = await Read(dir, "language-month");
            var english = await Read(dir, "en", "language-month");

            Assert.Equal("2026. október", MonthTitle.In(hungarian));
            Assert.Equal("October 2026", MonthTitle.In(english));
            Assert.True(File.Exists(Path.Combine(dir, "en", "index.html")), "the English root was not written");
            Assert.True(File.Exists(Path.Combine(dir, "index.html")), "the default language's root was not written");
        }
        finally
        {
            Clean(dir);
        }
    }

    [Fact]
    public async Task Each_page_says_its_own_language_over_the_boot_shells()
    {
        var dir = await PublishAsync();

        try
        {
            var hungarian = await Read(dir, "language-month");
            var english = await Read(dir, "en", "language-month");

            Assert.Contains("lang=\"hu\"", HtmlTag(hungarian), StringComparison.Ordinal);
            Assert.Contains("lang=\"en\"", HtmlTag(english), StringComparison.Ordinal);
            Assert.DoesNotContain("lang=\"en\"", HtmlTag(hungarian), StringComparison.Ordinal);
        }
        finally
        {
            Clean(dir);
        }
    }

    [Fact]
    public async Task Each_page_names_itself_canonical_and_every_language_as_an_alternate()
    {
        var dir = await PublishAsync();

        try
        {
            var english = await Read(dir, "en", "language-month");

            Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/en/language-month/\">", english, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"hu\" href=\"https://example.com/language-month/\">", english, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en/language-month/\">", english, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/language-month/\">", english, StringComparison.Ordinal);
        }
        finally
        {
            Clean(dir);
        }
    }

    [Fact]
    public async Task A_link_on_a_page_stays_in_that_pages_language()
    {
        var dir = await PublishAsync();

        try
        {
            var hungarian = await Read(dir, "language-month");
            var english = await Read(dir, "en", "language-month");

            Assert.Contains("href=\"/language-month\"", hungarian, StringComparison.Ordinal);
            Assert.Contains("href=\"/en/language-month\"", english, StringComparison.Ordinal);
        }
        finally
        {
            Clean(dir);
        }
    }

    [Fact]
    public async Task The_sitemap_lists_every_language_with_its_alternates()
    {
        var dir = await PublishAsync();

        try
        {
            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"), TestContext.Current.CancellationToken);

            Assert.Contains("xmlns:xhtml=\"http://www.w3.org/1999/xhtml\"", sitemap, StringComparison.Ordinal);
            Assert.Contains("<loc>https://example.com/en/language-month/</loc>", sitemap, StringComparison.Ordinal);
            Assert.Contains(
                "<url><loc>https://example.com/language-month/</loc>"
                + "<xhtml:link rel=\"alternate\" hreflang=\"hu\" href=\"https://example.com/language-month/\"/>"
                + "<xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en/language-month/\"/>"
                + "<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/language-month/\"/></url>",
                sitemap,
                StringComparison.Ordinal);
        }
        finally
        {
            Clean(dir);
        }
    }

    // One publish of a two-language app into a fresh directory holding a boot shell, answering the directory.
    private static async Task<string> PublishAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "index.html"), Shell, TestContext.Current.CancellationToken);

        using var buildMachine = ProcessCulture.Set("en-US");
        RaskCulture.ResetForTests();
        RouteRegistry.Replace(RouteGroup, [
            new RouteRegistration(typeof(LanguagePage), "/", null),
            new RouteRegistration(typeof(LanguagePage), "/language-month", null),
        ]);
        Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, dir);
        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");

        try
        {
            await WasmHostBuilder.CreateDefault()
                .UseCulture(c =>
                {
                    c.SupportedCultures.Add("hu");
                    c.SupportedCultures.Add("en");
                    c.UrlPrefix = true;
                })
                .Run<LanguagePage>();
            return dir;
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, null);
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            RouteRegistry.Replace(RouteGroup, []);
            RaskCulture.ResetForTests();
        }
    }

    private static Task<string> Read(string dir, params string[] route) =>
        File.ReadAllTextAsync(Path.Combine([dir, .. route, "index.html"]), TestContext.Current.CancellationToken);

    private static string HtmlTag(string html)
    {
        var tag = html[html.IndexOf("<html", StringComparison.Ordinal)..];
        return tag[..(tag.IndexOf('>', StringComparison.Ordinal) + 1)];
    }

    private static void Clean(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    // A page with a month title in its culture's own pattern and a link to itself.
    internal sealed partial class LanguagePage : Component
    {
        protected override Component? Render() =>
            Div[
                H2[new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified).ToString("Y", CultureInfo.CurrentCulture)],
                A.Href(new RouteUrl("/language-month", null, typeof(LanguagePage)))["month"]
            ];
    }
}
