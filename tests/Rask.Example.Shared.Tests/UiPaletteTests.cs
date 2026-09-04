using System.Text.RegularExpressions;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests;

/// <summary>
/// The showcase's copy of the kit's palette matches the kit's.
/// </summary>
/// <remarks>
/// <para>
/// Tailwind only emits a utility whose token it can see, and it sees tokens declared in the project it
/// is run from. The kit ships a compiled sheet carrying the utilities its OWN components use; the
/// showcase writes different ones (<c>bg-ui-brand/10</c>, <c>ring-ui-line/40</c>,
/// <c>text-ui-warn-ink</c>), so the tokens have to exist in the showcase's own <c>@theme</c> too.
/// </para>
/// <para>
/// Sharing one file instead was tried, and does not work. A <c>@theme</c> reached through an
/// <c>@import</c> after the Tailwind entry points is not interpreted as theme: a literal
/// <c>@theme { … }</c> survives into the output, which a browser ignores, and the <c>ui-*</c> namespace
/// is never registered — so every utility naming it silently vanishes while the stylesheet still looks
/// correct and the build stays green. It emptied the kit's own sheet as well as this one.
/// </para>
/// <para>
/// So the palette is copied, and this is what stops the copy drifting. A copy is survivable; a copy
/// that drifts is a showcase whose colours disagree with the operator console for a reason nobody can
/// see in a diff.
/// </para>
/// </remarks>
public sealed class UiPaletteTests
{
    private static readonly Regex Token =
        new(@"(?<name>--color-ui-[a-z-]+)\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled);

    [Fact]
    public void The_showcase_declares_every_token_the_kit_does_with_the_same_value()
    {
        var kit = Tokens(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "ui.css"));
        var showcase = Tokens(
            Path.Combine(RepoRoot(), "samples", "Rask.Example.Shared", "Styles", "app.css"));

        // Guard the extractor. A regex that stopped matching would make every comparison below vacuous,
        // which is the exact shape of failure this file is about.
        Assert.True(kit.Count >= 13, $"only {kit.Count} token(s) parsed out of the kit's palette.");

        var missing = kit.Keys.Where(k => !showcase.ContainsKey(k)).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            $"the showcase's @theme is missing {string.Join(", ", missing)}, so every utility naming "
            + "them is silently dropped from its stylesheet.");

        var differing = kit
            .Where(kv => showcase[kv.Key] != kv.Value)
            .Select(kv => $"{kv.Key}: kit={kv.Value} showcase={showcase[kv.Key]}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            differing.Length == 0,
            "the showcase's palette has drifted from the kit's: " + string.Join("; ", differing));
    }

    private static Dictionary<string, string> Tokens(string path)
    {
        var text = File.ReadAllText(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in Token.Matches(text))
        {
            // Last declaration wins, exactly as the cascade would read it.
            result[m.Groups["name"].Value] = m.Groups["value"].Value.Trim();
        }

        return result;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not find the repository root.");
    }

    [Fact]
    public void The_document_opts_into_the_kits_theme_scope()
    {
        // Load-bearing, and silent when missing. The kit scopes daisyUI's theme to this attribute so that
        // referencing the package cannot repaint an application that only wanted a button — which means
        // every token above resolves to nothing until something in the ancestry carries it. Structure and
        // layout survive without it, colour does not, and nothing that reads class names can tell.
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default()).Html;

        Assert.Matches(new Regex(@"<html[^>]*\sdata-rask-ui\b"), html);
    }


    [Fact]
    public void The_document_inlines_the_kits_stylesheet()
    {
        // The tokens above are expressed in daisyUI's variables, and those are defined ONLY in the kit's
        // compiled sheet — Tailwind scans the project it runs in, so this app's own build cannot emit
        // them. Without the sheet on the page every colour resolves to nothing: not wrong, absent.
        // Structure and layout survive it, which is exactly why it went unnoticed until a browser test
        // compared --accent with --color-ui-brand and found both empty.
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default()).Html;

        Assert.Contains("--color-primary", html, StringComparison.Ordinal);
    }

}
