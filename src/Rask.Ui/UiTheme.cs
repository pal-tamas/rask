namespace Rask.Ui;

/// <summary>
/// Turns a <see cref="UiThemeName" /> into the string daisyUI matches on.
/// </summary>
public static class UiTheme
{
    /// <summary>
    /// The <c>data-theme</c> value for <paramref name="theme" /> — its name, lowercased.
    /// </summary>
    /// <remarks>
    /// daisyUI's own names are all lowercase single words, so the mapping is mechanical rather than a
    /// table that could drift out of step with the enum. <c>UiThemeTests</c> holds every member to a
    /// theme the compiled stylesheet actually defines, so a member added here without the sheet
    /// shipping it fails the build's tests rather than a page.
    /// </remarks>
    public static string Value(UiThemeName theme) =>
        theme.ToString().ToLowerInvariant();

    /// <summary>Every theme the kit ships, in declaration order.</summary>
    public static IReadOnlyList<UiThemeName> All { get; } = Enum.GetValues<UiThemeName>();
}
