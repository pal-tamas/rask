using System.Text.RegularExpressions;

namespace Rask.Cli.Tests;

/// <summary>
///     No template draws with daisyUI: the C# pages use the kit's components and plain Tailwind, and the
///     front ends plain Tailwind.
/// </summary>
/// <remarks>
///     A daisyUI class left in a template keeps rendering for as long as the kit still ships the plugin,
///     and turns into unstyled text the day it stops — with the build green, because a class nothing
///     defines is still a valid string. So the tree is read as text here, every file a scaffold can write.
/// </remarks>
public sealed partial class TemplateDaisyUiFreeTests
{
    private static readonly string RepoRoot = CliBuildE2E.FindRepoRoot();

    private static readonly string[] Drawn = [".cs", ".css", ".html", ".ts", ".tsx", ".vue", ".svelte", ".razor"];

    // daisyUI's component classes, as whole tokens of a class attribute.
    private static readonly HashSet<string> Components = new(StringComparer.Ordinal)
    {
        "btn", "card", "card-body", "card-actions", "card-title", "hero", "hero-content", "navbar", "navbar-start",
        "navbar-center", "navbar-end", "footer-center", "kbd", "alert", "fieldset", "fieldset-legend", "divider",
        "loading", "loading-spinner", "link", "link-primary", "link-hover", "input", "checkbox", "list", "list-row",
        "rounded-box", "badge", "menu", "join", "stat", "label",
    };

    [Fact]
    public void No_template_names_daisyUI_or_its_theme_colours()
    {
        var files = TemplateFiles();

        var offenders = files
            .Where(f => DaisyName().IsMatch(f.Text) || ThemeColour().IsMatch(f.Text))
            .Select(f => f.Path)
            .ToArray();

        Assert.True(offenders.Length == 0, "daisyUI or its theme colours in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_template_class_is_a_daisyUI_component()
    {
        var files = TemplateFiles();

        var offenders = files
            .SelectMany(f => ClassLiterals(f).SelectMany(Tokens).Where(Components.Contains).Select(t => $"{f.Path}: {t}"))
            .ToArray();

        Assert.True(offenders.Length == 0, "daisyUI component classes: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> ClassLiterals((string Path, string Text) file) =>
        file.Path.EndsWith(".cs", StringComparison.Ordinal)
            ? CsClass().Matches(file.Text).Select(m => m.Groups[1].Value)
            : MarkupClass().Matches(file.Text).Select(m => m.Groups[1].Value);

    // A variant prefix (`dark:`, `hover:`) is not part of the name.
    private static IEnumerable<string> Tokens(string classes) =>
        classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t[(t.LastIndexOf(':') + 1)..]);

    private static (string Path, string Text)[] TemplateFiles()
    {
        var root = Path.Combine(RepoRoot, "src", "Rask.Templates");

        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Drawn.Contains(Path.GetExtension(p), StringComparer.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => (Path.GetRelativePath(root, p), File.ReadAllText(p)))
            .ToArray();

        Assert.NotEmpty(files);

        return files;
    }

    [GeneratedRegex(@"daisy", RegexOptions.IgnoreCase)]
    private static partial Regex DaisyName();

    // base-100/200/300, base-content, and the kit's old `*-ui-*` theme tokens.
    [GeneratedRegex(@"\b(bg|text|border|ring|divide|outline|fill|stroke|from|via|to)-(base|ui)-")]
    private static partial Regex ThemeColour();

    [GeneratedRegex("""\.Class\("([^"]*)"\)""")]
    private static partial Regex CsClass();

    // `class="…"` and `className="…"`, not a framework's bound `:class="expression"`.
    [GeneratedRegex(@"(?<![:\w-])(?:class|className)=""([^""]*)""")]
    private static partial Regex MarkupClass();
}
