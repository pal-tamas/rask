namespace Rask.Cli;

/// <summary>
///     What the CLI needs Node.js to be, in one place.
/// </summary>
/// <remarks>
///     <para>
///         Two different numbers, and conflating them is what issue #886 was about.
///     </para>
///     <para>
///         <see cref="BuildFloor" /> is the <b>minimum an app with islands builds on</b>: the
///         <c>RaskExternalMinimumNode</c> that <c>Rask.External.props</c> declares and its targets enforce
///         as RASKISLAND001. It is deliberately a floor and not a recommendation — Vite asks for
///         <c>^20.19.0 || &gt;=22.12.0</c>, and 22.12.0 is the lowest version satisfying that with no
///         hole. Raising it would break apps that build fine today, so it is mirrored here, never led
///         from here; <c>NodeRequirementTests</c> fails if the two ever disagree.
///     </para>
///     <para>
///         <see cref="ScaffoldLine" /> is the <b>current Active LTS</b>: what <c>rask.sh</c> and
///         <c>rask.ps1</c> install and what the docs recommend.
///     </para>
/// </remarks>
internal static class NodeRequirement
{
    /// <summary>
    ///     The lowest Node an app with islands builds on. Mirrors <c>RaskExternalMinimumNode</c> in
    ///     <c>src/Rask.External/build/Rask.External.props</c>, which is the enforcing copy — and
    ///     <c>RaskSpaMinimumNode</c> in <c>Rask.Spa.Hosting.props</c> (RASKSPA005), for a front-end template.
    /// </summary>
    public static readonly Version BuildFloor = new(22, 12, 0);

    /// <summary>
    ///     The Node LTS line the installers put on a machine. 24 is "Krypton", Active LTS since 2025-10.
    /// </summary>
    public static readonly Version ScaffoldLine = new(24, 15, 0);

    /// <summary>How to get it, phrased the same way everywhere the CLI has to say it.</summary>
    public const string InstallHint =
        "Install the current Node LTS from https://nodejs.org "
        + "(macOS: brew install node; Windows: winget install OpenJS.NodeJS.LTS; "
        + "Linux: your distro's nodejs package), or let `rask.sh` do it.";

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
