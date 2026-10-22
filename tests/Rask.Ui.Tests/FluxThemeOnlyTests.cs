using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     Rask.Ui draws with Flux UI's theme alone: the zinc scale, the accent and a <c>dark</c> class. The library it
///     replaced (daisyUI) is gone, and nothing that shipped may reach for it again.
/// </summary>
/// <remarks>
///     A class or a token from it would compile to nothing now, silently — the markup right and the page unstyled. So
///     the words are what is looked for: its name, the attribute that scoped its theme, the attribute that picked
///     one, and the kit's transitional tokens over its palette.
/// </remarks>
public sealed partial class FluxThemeOnlyTests
{
    [Fact]
    public void Nothing_that_ships_names_the_old_theme_library_or_its_attributes_and_tokens()
    {
        var src = Path.Combine(RepoRoot.FullPath, "src");

        var offenders = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(Shipped)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, at) => (line, at))
                .Where(l => Retired().IsMatch(l.line))
                .Select(l => $"{Path.GetRelativePath(src, file)}:{l.at + 1}: {Retired().Match(l.line).Value}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Retired theme vocabulary in src/:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void An_apps_own_accent_wins_over_the_kits_in_light_and_dark()
    {
        // Flux's theming docs, as an app writes them, after the kit's import (TA's amber theme).
        const string Theme = """
            @theme {
              --color-accent: var(--color-amber-400);
              --color-accent-content: var(--color-amber-700);
              --color-accent-foreground: var(--color-amber-950);
            }

            @layer theme {
              .dark {
                --color-accent: var(--color-amber-300);
                --color-accent-content: var(--color-amber-400);
                --color-accent-foreground: var(--color-amber-950);
              }
            }
            """;

        var css = KitConsumer.Compile(KitConsumer.Import + "\n" + Theme, "bg-accent text-accent-foreground");

        Assert.Equal("var(--color-amber-400)", Last(css, ":root,:host", "--color-accent"));
        Assert.Equal("var(--color-amber-300)", Last(css, ".dark", "--color-accent"));
        Assert.Equal("var(--color-amber-950)", Last(css, ".dark", "--color-accent-foreground"));
        Assert.Matches(@"\.bg-accent\s*\{\s*background-color:\s*var\(--color-accent\)", css);
    }

    // The value the LAST rule with this exact selector gives the property: the one the cascade keeps, both rules
    // being in the `theme` layer and of one specificity.
    private static string Last(string css, string selector, string property) =>
        Regex.Matches(css, Regex.Escape(selector).Replace(",", @",\s*") + @"\s*\{([^}]*)\}")
            .Select(rule => Regex.Match(rule.Groups[1].Value, Regex.Escape(property) + @"\s*:\s*([^;]+?)\s*(;|$)"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .LastOrDefault() ?? "";

    private static bool Shipped(string file)
    {
        var path = file.Replace('\\', '/');
        return !path.Contains("/obj/", StringComparison.Ordinal)
               && !path.Contains("/bin/", StringComparison.Ordinal)
               && !path.Contains("/node_modules/", StringComparison.Ordinal)
               && !path.Contains("/Styles/vendor/", StringComparison.Ordinal)
               && !path.Contains("/wwwroot/css/", StringComparison.Ordinal)
               && !path.Contains("/wwwroot/_framework/", StringComparison.Ordinal)
               && Path.GetExtension(path) is ".cs" or ".css" or ".ts" or ".js" or ".md" or ".html" or ".txt"
                   or ".props" or ".targets" or ".csproj" or ".json" or ".mjs";
    }

    // Built from parts so this file is no offender of its own scan when a copy of it is.
    [GeneratedRegex("(?i:" + "dai" + "sy)" + @"|data-rask-ui(?![\w-])|data-theme\b|(?<![\w-])(?:[a-z0-9-]+:)*(?:bg|text|border|ring|divide)-ui-(?:bg|panel|well|line|ink|muted|brand|ok|warn|danger|info)\b|--color-ui-|--color-base-|fx-accent")]
    private static partial Regex Retired();
}
