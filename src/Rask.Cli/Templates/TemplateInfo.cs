namespace Rask.Cli.Templates;

/// <summary>
/// A Rask project template: its friendly <see cref="Key"/> (what the user types after
/// <c>rask new --template</c>), a human <see cref="DisplayName"/>, and the opt-in feature
/// <see cref="SupportedFlags"/> that template understands.
/// </summary>
internal sealed record TemplateInfo(
    string Key,
    string DisplayName,
    IReadOnlySet<string> SupportedFlags,
    bool ShipsLocalization = false)
{
    /// <summary>
    ///     Whether this template scaffolds the language list and an English string catalog.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not a flag, and deliberately so (#854). The languages an app ships are listed in
    ///         <c>appsettings.json</c>'s <c>Rask:Cultures</c> — a new project starts with English, and adding a
    ///         second one is an entry in the list it already has. There is nothing for a
    ///         command line to decide, so <c>--culture</c> and <c>--no-localization</c> are gone rather
    ///         than kept as flags that restate what the file already says.
    ///     </para>
    ///     <para>
    ///         Every template but the TypeScript front ends ships it. On browser-WASM the list lives in
    ///         <c>Rask:Cultures</c> too (<c>wwwroot/appsettings.json</c>, or the host's file for a hosted client),
    ///         and the build ships ICU — about a megabyte — only once it names a language other than English, so
    ///         the English a new app starts with costs nothing.
    ///     </para>
    /// </remarks>
    public bool ShipsLocalization { get; } = ShipsLocalization;
}
