namespace Rask.Cli;

/// <summary>
///     What the CLI needs Node.js to be, in one place.
/// </summary>
/// <remarks>
///     <para>
///         Two different numbers, and conflating them is what issue #886 was about.
///     </para>
///     <para>
///         <see cref="BuildFloor" /> is the <b>minimum an existing app builds on</b>: the
///         <c>RaskSpaMinimumNode</c> that <c>Rask.Spa.Hosting.props</c> declares and its targets enforce
///         as RASKSPA005. It is deliberately a floor and not a recommendation — Vite asks for
///         <c>^20.19.0 || &gt;=22.12.0</c>, and 22.12.0 is the lowest version satisfying that with no
///         hole. Raising it would break apps that build fine today, so it is mirrored here, never led
///         from here; <c>NodeRequirementTests</c> fails if the two ever disagree.
///     </para>
///     <para>
///         <see cref="ScaffoldLine" /> is the <b>Node line `rask new` wants when it scaffolds</b>, and
///         it is higher on purpose. Scaffolding a front-end template shells out to somebody else's
///         current CLI — <c>create-vite@latest</c>, <c>@angular/cli@latest</c> — and those track the
///         Active LTS and raise their own floors whenever they like. Angular's CLI already refuses
///         below <c>^22.22.3 || ^24.15.0 || &gt;=26.0.0</c>, which a machine satisfying only
///         <see cref="BuildFloor" /> does not meet: the scaffold then fails at exit 1 <i>after</i> the
///         project directory exists, having told the user 22.12 was enough.
///     </para>
///     <para>
///         So the answer to "which version" is the current Active LTS, which is what the scaffolders
///         themselves target and what <c>rask.sh</c> installs. Stating the LTS line rather than pinning
///         the external CLIs is deliberate: pinning them would freeze every generated project on a
///         scaffolder that ages out, and the templates are meant to be whatever those tools ship today.
///     </para>
/// </remarks>
internal static class NodeRequirement
{
    /// <summary>
    ///     The lowest Node an already-scaffolded app builds on. Mirrors <c>RaskSpaMinimumNode</c> in
    ///     <c>src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props</c>, which is the enforcing copy.
    /// </summary>
    public static readonly Version BuildFloor = new(22, 12, 0);

    /// <summary>
    ///     The Node LTS line <c>rask new</c> asks for, and the one <c>rask doctor</c> measures against.
    ///     24 is "Krypton", Active LTS since 2025-10; its 24.15.0 also clears Angular's CLI floor.
    /// </summary>
    public static readonly Version ScaffoldLine = new(24, 15, 0);

    /// <summary>How to get it, phrased the same way everywhere the CLI has to say it.</summary>
    public const string InstallHint =
        "Install the current Node LTS from https://nodejs.org "
        + "(macOS: brew install node; Windows: winget install OpenJS.NodeJS.LTS; "
        + "Linux: your distro's nodejs package), or let `rask.sh` do it.";

    /// <summary>The message shown when shelling out to an external scaffolder could not start.</summary>
    public static string ScaffoldHint(string scaffolderName) =>
        $"Scaffolding runs {scaffolderName}, which needs Node.js {ScaffoldLine.Major} LTS or newer "
        + $"(v{ScaffoldLine} at the lowest — Angular's CLI refuses below it). " + InstallHint;

    /// <summary>
    ///     Reads a version out of whatever a tool printed. <c>node --version</c> answers <c>v24.20.0</c>,
    ///     <c>npm --version</c> answers <c>11.19.0</c>, and <c>dotnet --version</c> can answer
    ///     <c>10.0.100-preview.3.25201.16</c> — so the leading <c>v</c> and any pre-release suffix are
    ///     both stripped before parsing rather than being allowed to fail the parse and report a present
    ///     tool as missing.
    /// </summary>
    public static Version? Parse(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return null;
        }

        var span = reported.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        var cut = span.AsSpan().IndexOfAny('-', '+', ' ');
        if (cut >= 0)
        {
            span = span[..cut];
        }

        // Version.TryParse rejects a bare major ("24"), which is a shape `dotnet --list-sdks` style
        // output can take, so a single component is padded rather than discarded.
        if (!span.Contains('.'))
        {
            span += ".0";
        }

        return Version.TryParse(span, out var version) ? version : null;
    }
}
