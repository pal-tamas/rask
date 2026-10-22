using System.Text.RegularExpressions;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests;

/// <summary>
/// Every app that draws with <c>Rask.Ui</c> and runs Tailwind is wired for it: its ONE stylesheet takes
/// the kit in with a single import, it links no second sheet, and its pages start dark mode before they paint.
/// </summary>
/// <remarks>
/// <para>
/// All three fail silently in the same way: the build stays green, the class names in the markup stay
/// correct, and the page arrives wrong.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The import.</b> The kit's class names are string literals in a compiled assembly, where no scan
/// finds them, and its palette is a <c>@theme</c> Tailwind has to see to emit a utility against.
/// <c>@import "./vendor/rask-ui.css"</c> brings both — the palette used to be copied into this app's
/// own <c>@theme</c> by hand, and a test held the copy equal to the kit's.
/// </item>
/// <item>
/// <b>One sheet.</b> The kit's precompiled sheet linked beside the app's own is two
/// <c>@layer utilities</c> ranked by link order alone: this app's <c>bg-white</c> beat the kit's
/// <c>dark:bg-white/4</c>, and a Flux card was white in dark mode.
/// </item>
/// <item>
/// <b>Dark mode.</b> The kit's <c>dark:</c> is Flux's <c>dark</c> class, which only the appearance script
/// puts on <c>&lt;html&gt;</c>; a page without it never turns dark, and its accent never flips.
/// </item>
/// </list>
/// </remarks>
public sealed class UiKitWiringTests
{
    private static readonly Regex Token =
        new(@"(?<name>--color-accent[a-z0-9-]*)\s*:", RegexOptions.Compiled);

    // The apps that reference Rask.Ui AND compile a stylesheet of their own. A new one added without
    // the import fails here rather than on a deployed page.
    public static TheoryData<string> KitApps() =>
    [
        Path.Combine("src", "Rask.Site"),
    ];

    [Theory]
    [MemberData(nameof(KitApps))]
    public void Every_kit_app_takes_the_kit_in_with_one_import(string appDir)
    {
        var sheet = Code(Path.Combine(RepoRoot(), appDir, "Styles", "app.css"));

        Assert.True(
            Regex.IsMatch(sheet, @"^\s*@import ""\./vendor/rask-ui\.css"";"),
            $"{appDir}/Styles/app.css must open with `@import \"./vendor/rask-ui.css\";` — that file declares "
            + "the layer order, so it has to be the first at-rule, and it is how this app's ONE Tailwind "
            + "build compiles the kit's classes beside its own.");

        // The import brings Tailwind itself. A second copy emits every utility twice.
        Assert.DoesNotContain("@import \"tailwindcss\"", sheet, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(KitApps))]
    public void No_kit_app_keeps_a_copy_of_what_the_import_carries(string appDir)
    {
        // The palette used to be copied into each app's @theme by hand and held equal to the kit's by a
        // test, and `dark` was defined in both sheets. Each has ONE declaration now, in the kit's ui.css;
        // a copy here would be a second one to drift.
        var kit = Tokens(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "ui.css")));
        var sheet = Code(Path.Combine(RepoRoot(), appDir, "Styles", "app.css"));

        // Guard the extractor: a regex that stopped matching would make this vacuous.
        Assert.True(kit.Count >= 3, $"only {kit.Count} token(s) parsed out of the kit's theme.");

        var copied = kit.Intersect(Tokens(sheet), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            copied.Length == 0,
            $"{appDir}/Styles/app.css declares {string.Join(", ", copied)} itself. The kit's import already does.");

        Assert.DoesNotContain("@custom-variant dark", sheet, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"@layer\s+[a-z, -]+;"), sheet);
    }

    [Theory]
    [MemberData(nameof(KitApps))]
    public void No_kit_app_also_links_the_precompiled_kit_sheet(string appDir)
    {
        // The bug this arrangement ends: two sheets, each with an `@layer utilities`, ranked by link
        // order. This app's `bg-white` beat the kit's `dark:bg-white/4` and a Flux card stayed white in
        // dark mode; its `flex-col` beat the kit's `sm:flex-row`. The build refuses the property; the
        // link in App.cs is the half only a reader of the source can see.
        var csproj = Directory.EnumerateFiles(Path.Combine(RepoRoot(), appDir), "*.csproj").Single();
        var app = File.ReadAllText(Path.Combine(RepoRoot(), appDir, "App.cs"));

        Assert.DoesNotContain("RaskUiWriteStylesheet", File.ReadAllText(csproj), StringComparison.Ordinal);
        Assert.DoesNotContain("UiStylesheet.Href", app, StringComparison.Ordinal);
        Assert.DoesNotContain("UiStylesheet.Css", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_links_one_stylesheet_that_carries_the_kit()
    {
        var html = Page.RenderDocument(new global::Rask.Site.App(), TestServices.Default()).Html;

        Assert.Contains("/css/app.css", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-ui.css", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_starts_dark_mode_once_before_its_stylesheet()
    {
        var html = Page.RenderDocument(new global::Rask.Site.App(), TestServices.Default()).Html;

        var script = html.IndexOf("data-rask-appearance=\"", StringComparison.Ordinal);

        Assert.True(script >= 0, "the document has no appearance script, so it never turns dark.");
        Assert.Equal(script, html.LastIndexOf("data-rask-appearance=\"", StringComparison.Ordinal));
        Assert.True(script < html.IndexOf("/css/app.css", StringComparison.Ordinal), "the script runs after the stylesheet.");
        Assert.DoesNotMatch(new Regex(@"<html[^>]*\sdata-rask-ui\b"), html);
    }

    private static HashSet<string> Tokens(string css) =>
        Token.Matches(css).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    // The sheet without its comments, so prose about an import is not read as one.
    private static string Code(string path) =>
        Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string RepoRoot() => RepoPaths.Root;
}
