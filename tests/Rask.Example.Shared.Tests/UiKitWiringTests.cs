using System.Text.RegularExpressions;

namespace Rask.Example.Shared.Tests;

/// <summary>
/// Every app that draws with <c>Rask.Ui</c> is wired for it: it copies the kit's palette into its own
/// <c>@theme</c>, and it turns the kit's theme scope on.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the landing site shipped to rask.sh with neither, and nothing noticed. Both
/// failures are silent in exactly the same way: the build stays green, the class names in the markup
/// stay correct, and the page arrives with no colour. <c>UiPaletteTests</c> guarded the showcase's copy
/// alone, so the second consumer of the kit reproduced the bug the first one had already solved.
/// </para>
/// <para>
/// The two halves are separate failures with the same symptom, which is why both are checked:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The palette copy.</b> Tailwind emits a utility only where it can see its token, and a
/// <c>@theme</c> reached through an <c>@import</c> after the Tailwind entry points is not read as
/// theme. Without the copy the utilities are never generated — the site's sheet had none of
/// <c>bg-ui-bg</c>, <c>text-ui-ink</c> or <c>border-ui-line</c> while its markup used all three.
/// </item>
/// <item>
/// <b>The theme scope.</b> The kit confines daisyUI's theme to <c>data-rask-ui</c> so that referencing
/// the package cannot repaint an app that only wanted a button. Every kit token is an alias onto
/// <c>--color-base-*</c>, which is defined only inside that scope — so without the attribute the
/// utilities generate and then resolve to nothing.
/// </item>
/// </list>
/// <para>
/// Values are NOT required to match here: an app may deliberately redefine a token, as the landing site
/// does for the brand accent. <c>UiPaletteTests</c> holds the showcase to the stricter same-value rule.
/// What is required is that every token the kit declares is declared, so nothing is silently dropped.
/// </para>
/// </remarks>
public sealed class UiKitWiringTests
{
    private static readonly Regex Token =
        new(@"(?<name>--color-ui-[a-z0-9-]+)\s*:", RegexOptions.Compiled);

    // The sample apps that reference Rask.Ui AND compile a stylesheet of their own. A new one added
    // without its palette copy fails here rather than on a deployed page.
    public static TheoryData<string> KitApps() =>
    [
        Path.Combine("samples", "Rask.Example.Shared"),
        Path.Combine("samples", "Rask.Example.Site"),
    ];

    [Theory]
    [MemberData(nameof(KitApps))]
    public void EveryKitApp_CopiesEveryPaletteToken(string appDir)
    {
        var kit = Tokens(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "ui.css"));
        var app = Tokens(Path.Combine(RepoRoot(), appDir, "Styles", "app.css"));

        // Guard the extractor: a regex that stopped matching would make this vacuous, which is the
        // exact shape of failure this file is about.
        Assert.True(kit.Count >= 13, $"only {kit.Count} token(s) parsed out of the kit's palette.");

        var missing = kit.Where(t => !app.Contains(t)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            $"{appDir}/Styles/app.css is missing {string.Join(", ", missing)} from its @theme, so every "
            + "utility naming them is silently dropped from its stylesheet and the page renders "
            + "structurally correct with no colour.");
    }

    [Theory]
    [MemberData(nameof(KitApps))]
    public void EveryKitApp_TurnsTheThemeScopeOn(string appDir)
    {
        var root = Path.Combine(RepoRoot(), appDir, "App.cs");
        Assert.True(File.Exists(root), $"{appDir} has no App.cs to carry the theme scope.");

        var source = File.ReadAllText(root);

        Assert.True(
            source.Contains("ThemeScopeAttribute", StringComparison.Ordinal),
            $"{appDir}/App.cs never sets UiStylesheet.ThemeScopeAttribute, so daisyUI's theme is out of "
            + "scope for the whole document and every --color-ui-* token resolves to nothing. Put it on "
            + "<html> in a Shell override, as the showcase does.");
    }

    private static HashSet<string> Tokens(string path)
    {
        var text = File.ReadAllText(path);
        return Token.Matches(text).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
