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

    /// <summary>
    /// The value that means "follow the operating system" — <see cref="UiThemeName.System" />'s.
    /// </summary>
    /// <remarks>
    /// It is NOT a <c>data-theme</c> anything should be stamped with; daisyUI compiles no block for it,
    /// and stamping it leaves the document's colours undefined. It exists so a control can carry a value
    /// that a reader can select and a host can recognise as "remove the attribute" — see
    /// <see cref="UiThemePicker" />. Named here rather than spelled as a literal in each host because
    /// the recogniser and the control have to agree on it, and they are in different projects.
    /// </remarks>
    public const string SystemValue = "system";

    /// <summary>Every theme the kit ships, in declaration order.</summary>
    /// <remarks>
    /// <see cref="UiThemeName.System" /> is not among them: it names the absence of a choice rather than
    /// a palette, so a caller iterating this list to render one control per theme does not get a control
    /// for a <c>data-theme</c> that does not exist.
    /// </remarks>
    public static IReadOnlyList<UiThemeName> All { get; } =
        [.. Enum.GetValues<UiThemeName>().Where(t => t != UiThemeName.System)];
}
