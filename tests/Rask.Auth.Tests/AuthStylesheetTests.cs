using System.Text.RegularExpressions;
using Rask.Auth.Pages;

namespace Rask.Auth.Tests;

/// <summary>
///     The built-in pages carry their own stylesheet, and it has to contain what they write.
/// </summary>
/// <remarks>
///     <para>
///         These pages ship inside a package, so their class names live in a compiled assembly that no
///         application's Tailwind can scan — Tailwind emits a class only where it can SEE the name.
///         Borrowing the host's sheet is therefore not an option, and this project compiles its own at
///         its own build.
///     </para>
///     <para>
///         Every failure this file guards is silent. A class the sheet does not define is a correct
///         string in the markup naming a rule that exists nowhere, and the page renders structurally
///         perfect and unstyled on a green build.
///     </para>
/// </remarks>
public sealed class AuthStylesheetTests
{
    private static readonly string _css = Read();

    [Fact]
    public void TheSheetShippedAtAll() =>
        Assert.False(
            _css.Length == 0,
            "no compiled stylesheet is embedded in Rask.Auth, so every built-in page renders unstyled.");

    [Theory]
    [InlineData("card")]
    [InlineData("card-body")]
    [InlineData("card-actions")]
    [InlineData("hero")]
    [InlineData("btn")]
    [InlineData("input")]
    [InlineData("alert")]
    [InlineData("fieldset")]
    [InlineData("link")]
    public void EveryClassThePagesWriteIsDefined(string name) =>
        Assert.True(
            Regex.IsMatch(_css, $@"\.{Regex.Escape(name)}\s*[,{{]"),
            $".{name} is written by the built-in pages but is not in the sheet they carry, so it "
            + "styles nothing.");

    [Fact]
    public void ThePaletteIsScopedToThePagesOwnWrapper()
    {
        // Referencing a package must not repaint the application that referenced it. daisyUI paints
        // :root by default; this confines it to the attribute AuthPage puts on its own wrapper, which
        // is as high as a component can reach.
        Assert.Contains("data-rask-auth", _css, StringComparison.Ordinal);

        // Any rule that DEFINES a base colour must be inside that scope. Merely using one is fine —
        // that is the mechanism.
        foreach (Match block in Regex.Matches(_css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            if (block.Groups["body"].Value.Contains("--color-base-100:", StringComparison.Ordinal))
            {
                Assert.Contains("data-rask-auth", block.Groups["selector"].Value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ItIsFarSmallerThanTheWholeKit()
    {
        // The point of compiling here rather than carrying Rask.Ui's sheet: this one scans six pages,
        // so it holds what they use instead of every component daisyUI defines. That is what makes it
        // affordable to inline on a page that must render on an app with no CSS of its own.
        //
        // A generous ceiling — this is a guard against the sheet quietly becoming the whole library
        // (a lost @source exclusion, a safelist), not a byte budget.
        Assert.True(
            _css.Length < 120_000,
            $"the auth stylesheet is {_css.Length} bytes. It is inlined into every sign-in page, so at "
            + "this size something is emitting far more than the six pages use.");
    }

    [Fact]
    public void ItCarriesNoPreflightForTheHostsDocument()
    {
        // It is inlined into somebody else's page. A bare html/body rule here restyles the whole
        // application, which is the one thing a package's stylesheet must never do.
        foreach (Match block in Regex.Matches(_css, @"(?<selector>[^{}]+)\{[^{}]*\}"))
        {
            var selector = block.Groups["selector"].Value;
            Assert.False(
                Regex.IsMatch(selector, @"(^|,)\s*(html|body)\s*(,|$)"),
                $"'{selector.Trim()}' restyles the host's own document.");
        }
    }

    private static string Read()
    {
        using var stream = typeof(AuthPage).Assembly.GetManifestResourceStream("Rask.Auth.auth.css");
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
