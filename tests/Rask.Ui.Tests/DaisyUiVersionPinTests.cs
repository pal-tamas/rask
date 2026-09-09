using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     The daisyUI version the kit actually vendors, and the sentence in <c>ui.css</c> that names it.
/// </summary>
/// <remarks>
///     <para>
///         daisyUI is VENDORED: <c>Styles/vendor/daisyui.mjs</c> is the standalone bundle itself,
///         referenced by relative path from <c>@plugin</c> because Tailwind resolves a bare
///         <c>@plugin "daisyui"</c> the Node way and this project deliberately has no
///         <c>package.json</c>. So there is no npm ecosystem for Dependabot to watch and no version
///         string in any manifest — the pin is a 348 KB file, and the only place it states its version
///         is the <c>var version = "…"</c> inside it.
///     </para>
///     <para>
///         It is then stated a SECOND time, as prose, in the <c>@plugin</c> comment in <c>ui.css</c>. A
///         bump that replaces the bundle and forgets the sentence leaves the file claiming a version
///         the repository no longer ships, and nothing here noticed. That is this codebase's most
///         repeated failure shape — a claim written in a comment with nothing checking it — and
///         <c>TailwindVersionPinTests</c> is the same test for the other pinned compiler.
///     </para>
///     <para>
///         <see cref="The_shipped_sheet_was_compiled_by_the_bundle_this_version_came_from" /> is what
///         stops the rest being an agreement between two source files that may have compiled nothing.
///         The bundle names daisyUI's themes and the compiled sheet emits them, so comparing those two
///         sets is what ties the version the other tests check to the bytes an application receives —
///         a source file stating an intention is not evidence the shipped artifact carries it, which
///         is the same reason <c>UiLayerOrderTests</c> reads the compiled sheet rather than the
///         <c>@layer</c> line it was built from.
///     </para>
/// </remarks>
public sealed class DaisyUiVersionPinTests
{
    /// <summary>The kit's stylesheet SOURCE — the file that names both the bundle and its version.</summary>
    private static readonly string _uiCssPath =
        Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Styles", "ui.css");

    private static readonly string _uiCss = File.ReadAllText(_uiCssPath);

    /// <summary>The same file with its comments stripped — the at-rules Tailwind actually acts on.</summary>
    /// <remarks>
    ///     The file EXPLAINS the resolution it avoids, quoting <c>@plugin "daisyui"</c> as the bare
    ///     specifier that fails. Matched against the raw text, the first <c>@plugin</c> found is that
    ///     sentence, and this suite went looking for a bundle called <c>daisyui</c> that never existed.
    /// </remarks>
    private static readonly string _uiCssCode =
        Regex.Replace(_uiCss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>The vendored bundle, read through the path <c>ui.css</c> names rather than a fixed one.</summary>
    private static readonly Lazy<string> _bundle = new(() => File.ReadAllText(BundlePath()));

    [Fact]
    public void The_css_points_at_a_vendored_bundle_that_is_there()
    {
        // Read out of ui.css rather than hard-coded, so this suite follows the @plugin wherever it
        // points. A test that reads a path the sheet no longer uses is checking a file the build does
        // not compile with.
        var declared = PluginPath();
        Assert.True(
            declared.StartsWith("./", StringComparison.Ordinal) || declared.StartsWith("../", StringComparison.Ordinal),
            $"ui.css loads the plugin as '{declared}'. It has to stay a RELATIVE path: the standalone "
            + "Tailwind engine bundles the compiler and carries no package tree, so a bare 'daisyui' "
            + "fails to resolve and this project has no node_modules to give it one.");

        Assert.True(
            File.Exists(BundlePath()),
            $"ui.css loads '{declared}', which resolves to '{BundlePath()}' and is not there. The kit "
            + "would compile with no daisyUI at all.");
    }

    [Fact]
    public void The_prose_in_the_css_names_the_version_the_bundle_actually_is()
    {
        // The defect this exists for: the bundle is re-downloaded and the sentence beside it is not.
        var claimed = Regex.Matches(_uiCss, @"daisyUI\s+(\d+\.\d+\.\d+)");
        Assert.True(
            claimed.Count == 1,
            $"ui.css names a daisyUI version {claimed.Count} time(s); expected exactly one. Two "
            + "sentences stating a version are two things to forget, and none at all removes the claim "
            + "this test is here to keep honest.");

        Assert.Equal(BundleVersion(), claimed[0].Groups[1].Value);
    }

    [Fact]
    public void The_bundle_still_carries_its_licence_header()
    {
        // The other thing a careless re-download drops. daisyUI is MIT and the kit redistributes the
        // bundle as build input, so the notice travelling with it is not decoration.
        var header = Head(_bundle.Value, 400);

        Assert.Contains("@license MIT", header, StringComparison.Ordinal);
        Assert.Contains("daisyUI", header, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_sheet_was_compiled_by_the_bundle_this_version_came_from()
    {
        // Without this the three tests above compare two files in the source tree and prove nothing
        // about what an application receives: a stale obj/ sheet, or an @plugin pointed somewhere
        // else, would leave every one of them green while the kit shipped a different daisyUI.
        //
        // The themes are the seam. ui.css asks for `themes: all`, so the bundle's own theme list and
        // the [data-theme=…] selectors in the compiled sheet must be the same set. They move together
        // on a real bump and disagree only when the sheet did not come from this bundle.
        var declared = BundleThemes();
        var emitted = Regex.Matches(UiStylesheet.Css, @"\[data-theme=([A-Za-z0-9_-]+)\]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Vacuous-pass guard on both sides: two empty sets are equal.
        Assert.NotEmpty(declared);
        Assert.NotEmpty(emitted);

        var missing = declared.Except(emitted, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = emitted.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            $"the shipped sheet does not carry the themes daisyUI {BundleVersion()} defines, so it was "
            + "not compiled by the vendored bundle and the version this suite checks is not the one an "
            + "application receives. Defined but not emitted: "
            + (missing.Length == 0 ? "(none)" : string.Join(", ", missing))
            + ". Emitted but not defined: "
            + (extra.Length == 0 ? "(none)" : string.Join(", ", extra))
            + ". ui.css asks for `themes: all`; if that changed on purpose, this test changes with it.");
    }

    /// <summary>The bundle path as <c>ui.css</c> spells it, e.g. <c>./vendor/daisyui.mjs</c>.</summary>
    private static string PluginPath()
    {
        var match = Regex.Match(_uiCssCode, @"@plugin\s+""([^""]+)""");
        Assert.True(match.Success, $"no `@plugin \"…\"` in {_uiCssPath}: the kit compiles without daisyUI.");
        return match.Groups[1].Value;
    }

    /// <summary>That path resolved against the directory <c>ui.css</c> lives in, as Tailwind resolves it.</summary>
    private static string BundlePath() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_uiCssPath)!, PluginPath()));

    private static string BundleVersion()
    {
        var match = Regex.Matches(_bundle.Value, @"\bvar version\s*=\s*""(\d+\.\d+\.\d+[^""]*)""");
        Assert.True(
            match.Count == 1,
            $"expected exactly one `var version = \"…\"` in the vendored bundle, found {match.Count}. "
            + "daisyUI's bundle states its version once; a different shape means this test is reading "
            + "the wrong number rather than that the pin is fine.");

        return match[0].Groups[1].Value;
    }

    /// <summary>Every theme name the bundle defines, taken from its own theme order.</summary>
    private static HashSet<string> BundleThemes()
    {
        var list = Regex.Match(_bundle.Value, @"themeOrder_default\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
        Assert.True(list.Success, "could not find daisyUI's theme list in the vendored bundle.");

        return Regex.Matches(list.Groups[1].Value, @"""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Head(string s, int length) => s.Length <= length ? s : s[..length];
}
