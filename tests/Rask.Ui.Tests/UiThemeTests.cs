using Rask.Ui;

namespace Rask.Ui.Tests;

/// <summary>
/// The typed theme names and the stylesheet agree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiThemeName" /> exists so that naming a theme is checked by the compiler rather than
/// spelled into a string — but the enum and the compiled sheet are produced by two different things
/// (a C# file and daisyUI's <c>themes:</c> option), so nothing but this keeps them in step.
/// </para>
/// <para>
/// The failure it prevents is silent in the usual way: an unmatched <c>data-theme</c> is not an error
/// anywhere, the attribute simply selects nothing and the palette stays where it was.
/// </para>
/// </remarks>
public sealed class UiThemeTests
{
    [Fact]
    public void EveryTypedTheme_IsDefinedByTheStylesheet()
    {
        var css = UiStylesheet.Css;
        Assert.False(string.IsNullOrEmpty(css), "the kit shipped no stylesheet to check against.");

        var missing = UiTheme.All
            .Select(UiTheme.Value)
            .Where(name => !css.Contains($"[data-theme={name}]", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "these are named by UiThemeName but not defined in the compiled sheet, so selecting one "
            + "changes nothing: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryThemeTheStylesheetDefines_HasATypedName()
    {
        // The other direction, so a theme daisyUI adds is not left unreachable from C#.
        var css = UiStylesheet.Css;
        var shipped = System.Text.RegularExpressions.Regex
            .Matches(css, @"\[data-theme=([a-z]+)\]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var typed = UiTheme.All.Select(UiTheme.Value).ToHashSet(StringComparer.Ordinal);
        var unreachable = shipped.Where(t => !typed.Contains(t)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            unreachable.Length == 0,
            "the stylesheet ships these themes with no UiThemeName member, so nothing can select them "
            + "in typed code: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void TheValueIsWhatDaisyUiMatchesOn()
    {
        // Lowercased member name, mechanically — the mapping is not a table that can drift.
        Assert.Equal("light", UiTheme.Value(UiThemeName.Light));
        Assert.Equal("cupcake", UiTheme.Value(UiThemeName.Cupcake));
        Assert.Equal("caramellatte", UiTheme.Value(UiThemeName.Caramellatte));
    }
}
