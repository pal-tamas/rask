using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Site.Features;
using Rask.Site.Features.UiDocs;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

/// <summary>The UI tab: its sidebar, its pages, the bar that marks it, and the family pages that send readers to it.</summary>
public sealed partial class UiDocsTests
{
    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in UiDocsCatalog.All)
        {
            data.Add(page.Path);
        }

        return data;
    }

    [Fact]
    public void The_component_pages_keep_the_order_of_the_Flux_catalogue()
    {
        var snapshot = File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json"));

        using var json = JsonDocument.Parse(snapshot);
        var flux = json.RootElement.GetProperty("pages").EnumerateArray()
            .Select(page => page.GetProperty("slug").GetString()!)
            .ToList();
        var ours = UiDocsCatalog.All.Where(page => page.Flux is not null).Select(page => page.Flux!).ToList();

        Assert.All(ours, slug => Assert.Contains(slug, flux));
        Assert.Equal(ours.OrderBy(flux.IndexOf), ours);
        Assert.Equal(UiDocsCatalog.All.Count, UiDocsCatalog.All.Select(page => page.Path.ToString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_page_sits_in_the_ui_sidebar_and_marks_the_ui_tab_current(string path)
    {
        var html = await SiteDocument.At(path);

        var sidebar = Regex.Match(html, "<nav[^>]*data-ui-navlist[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
        foreach (var page in UiDocsCatalog.All)
        {
            Assert.Contains($"href=\"{PageMeta.LinkTo(page.Path)}\"", sidebar, StringComparison.Ordinal);
        }

        var tab = Regex.Match(html, "<a[^>]*ui-tab[^>]*>").Value;
        Assert.Contains("aria-current=\"page\"", tab, StringComparison.Ordinal);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_docs_bar_offers_the_ui_tab_without_marking_it_current()
    {
        var html = await SiteDocument.At(global::Rask.Site.Routes.GuidesIndexPage());

        var tab = Regex.Match(html, "<a[^>]*ui-tab[^>]*>").Value;

        Assert.Contains($"href=\"{PageMeta.LinkTo(global::Rask.Site.Routes.UiIntroductionPage())}\"", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", tab, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task No_heading_on_a_ui_page_skips_a_level_below_the_one_before_it(string path)
    {
        var html = await SiteDocument.At(path);

        var main = Regex.Match(html, "<main[^>]*>.*</main>", RegexOptions.Singleline).Value;
        var levels = Regex.Matches(main, "<h([1-6])[\\s>]").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.Equal(1, levels[0]);
        Assert.All(levels.Zip(levels.Skip(1)), pair => Assert.True(pair.Second <= pair.First + 1, $"{path}: h{pair.First} is followed by h{pair.Second}"));
    }

    [Fact]
    public async Task An_example_inside_a_section_is_titled_one_level_below_the_section()
    {
        var html = await SiteDocument.At(global::Rask.Site.Routes.UiThemingPage());

        var section = Regex.Match(html, "<h([1-6])[^>]*>Accent color</h\\1>").Groups[1].Value;
        var example = Regex.Match(html, "<h([1-6])[^>]*>Accents</h\\1>").Groups[1].Value;

        Assert.Equal("2", section);
        Assert.Equal("3", example);
    }

    [Fact]
    public async Task Each_example_on_a_component_page_shows_its_own_code_right_above_its_result()
    {
        var html = await SiteDocument.At(global::Rask.Site.Routes.UiButtonPage());

        var variantsCode = html.IndexOf("ButtonVariantsExample.cs", StringComparison.Ordinal);
        var variants = html.IndexOf("data-testid=\"ui-button-variants\"", StringComparison.Ordinal);
        var sizesCode = html.IndexOf("ButtonSizesExample.cs", StringComparison.Ordinal);
        var reference = html.IndexOf("data-testid=\"ui-api\"", StringComparison.Ordinal);

        Assert.True(variantsCode >= 0 && variants > variantsCode, "the variants' code is not above their result");
        Assert.True(sizesCode > variants, "the next example's code does not follow the variants' result");
        Assert.True(reference > sizesCode, "the button page has no reference under its examples");
        Assert.Equal(12, Regex.Matches(html, "class=\"sample-card ").Count);
        Assert.DoesNotContain("ButtonSizesExample", html[variantsCode..variants], StringComparison.Ordinal);
        Assert.Contains("flux:button.group", html, StringComparison.Ordinal);
        Assert.Contains("Outline", html[reference..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/docs/ui/actions", "/ui/button/")]
    [InlineData("/docs/ui/actions", "/ui/dropdown/")]
    [InlineData("/docs/ui/actions", "/ui/context/")]
    [InlineData("/docs/ui/actions", "/ui/command/")]
    [InlineData("/docs/ui/actions", "/ui/popover/")]
    [InlineData("/docs/ui/actions", "/ui/modal/")]
    [InlineData("/docs/ui/data-display", "/ui/badge/")]
    [InlineData("/docs/ui/data-display", "/ui/card/")]
    [InlineData("/docs/ui/feedback", "/ui/callout/")]
    [InlineData("/docs/ui/layout", "/ui/separator/")]
    [InlineData("/docs/ui/layout", "/ui/heading/")]
    [InlineData("/docs/ui/layout", "/ui/text/")]
    public async Task A_family_page_links_to_the_pages_that_took_its_components(string family, string moved)
    {
        var html = await SiteDocument.At(family);

        var callout = Regex.Match(html, "data-testid=\"ui-moved\".*?</div>\\s*</div>", RegexOptions.Singleline).Value;

        Assert.Contains($"href=\"{moved}\"", html, StringComparison.Ordinal);
        Assert.NotEmpty(callout);
    }

    [Fact]
    public async Task The_emptied_actions_page_names_the_dropdown_page_canonical_and_leaves_the_docs_sidebar()
    {
        var html = await SiteDocument.At("/docs/ui/actions");
        var docs = await SiteDocument.At(global::Rask.Site.Routes.GuidesIndexPage());

        var link = Regex.Match(html, "<link[^>]*rel=\"canonical\"[^>]*>").Value;
        var canonical = Regex.Match(link, "href=\"([^\"]*)\"").Groups[1].Value;

        Assert.Equal(PageMeta.Origin + PageMeta.LinkTo(global::Rask.Site.Routes.UiDropdownPage()), canonical);
        Assert.DoesNotContain("href=\"/docs/ui/actions/\"", docs, StringComparison.Ordinal);
        Assert.Contains($"href=\"{PageMeta.LinkTo(global::Rask.Site.Routes.UiIntroductionPage())}\"", docs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_popover_example_inside_the_examples_section_is_titled_one_level_below_it()
    {
        var html = await SiteDocument.At(global::Rask.Site.Routes.UiPopoverPage());

        var section = Regex.Match(html, "<h([1-6])[^>]*>Examples</h\\1>").Groups[1].Value;
        var example = Regex.Match(html, "<h([1-6])[^>]*>Category picker</h\\1>").Groups[1].Value;

        Assert.Equal("2", section);
        Assert.Equal("3", example);
    }

    [Fact]
    public void The_llms_index_lists_every_ui_page_before_the_optional_section()
    {
        var index = LlmsText.Index();

        var ui = index.IndexOf("\n## UI components (Rask.Ui)\n", StringComparison.Ordinal);
        var optional = index.IndexOf("\n## Optional\n", StringComparison.Ordinal);

        Assert.True(ui > 0 && ui < optional, "the UI pages are not listed before the Optional section");
        Assert.All(UiDocsCatalog.All, page => Assert.Contains(
            $"- [{page.Title}]({PageMeta.Origin}{PageMeta.CanonicalPath(page.Path)}): {page.Description}\n",
            index,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_ui_page_breadcrumb_runs_through_the_ui_tab()
    {
        var crumbs = PageMeta.Breadcrumb("Button", PageMeta.CanonicalPath(global::Rask.Site.Routes.UiButtonPage()));

        Assert.Equal(["Rask", "UI", "Button"], crumbs.Select(crumb => crumb.Name));
        Assert.Equal(PageMeta.Origin + "/ui/", crumbs[1].Url);
    }
}
