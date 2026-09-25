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
    ///     Whether this template scaffolds the language registration in <c>Program.cs</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not a flag, and deliberately so (#854). The languages an app ships are configured in
    ///         <c>Program.cs</c> — a new project starts with English, and adding a second one is a line in
    ///         the <c>AddRask(configureCulture: ...)</c> call it already has. There is nothing for a
    ///         command line to decide, so <c>--culture</c> and <c>--no-localization</c> are gone rather
    ///         than kept as flags that restate what the file already says.
    ///     </para>
    ///     <para>
    ///         It stays off on browser-WASM because that is the one place naming a language is not free:
    ///         the browser needs ICU to resolve a culture at all, which is about a megabyte of extra
    ///         download (+32% on the showcase, measured brotli-to-brotli on a published trimmed build).
    ///         That is an opinion about the app rather than wiring, and it is also the one part
    ///         <c>Program.cs</c> cannot switch on by itself — <c>RaskGlobalization</c> is an MSBuild
    ///         property, scaffolded commented into the csproj with the reason beside it. On the server
    ///         the runtime carries ICU regardless, so it costs nothing and is standard.
    ///     </para>
    /// </remarks>
    public bool ShipsLocalization { get; } = ShipsLocalization;
}
