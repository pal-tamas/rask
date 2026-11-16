namespace Rask.Cli.Scaffolding;

/// <summary>
///     The .NET version a scaffolded project is built with: .NET 11.
/// </summary>
/// <remarks>
///     The committed template trees name the framework themselves — a csproj's
///     <c>&lt;TargetFramework&gt;</c>, the Docker image tags, the output path in <c>.vscode/launch.json</c>.
///     What is generated is the <c>global.json</c> beside them, from here.
/// </remarks>
internal static class DotnetTarget
{
    /// <summary>The version a scaffold's <c>global.json</c> names: the band's first RELEASED SDK.</summary>
    /// <remarks>
    ///     Never the SDK installed on the scaffolding machine, which would pin the output to whatever happened to
    ///     be there; the band's first GA release, so every later feature band and patch rolls forward onto it and
    ///     no release candidate does. A candidate sorts below the release it is a candidate for, and the
    ///     .NET 11 candidates carry the WebAssembly debug proxy CVE-2026-102870 was fixed in — a Debug build of a
    ///     browser app starts that proxy, so a scaffold must not build on one.
    /// </remarks>
    public const string SdkPin = "11.0.100";

    /// <summary>
    ///     The <c>global.json</c> every scaffold writes: the app is built by an SDK of its OWN major.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this file the SDK picks the newest one installed, which on a machine that also has
    ///         the next major in preview means a <c>net11.0</c> app is compiled by a <c>12.0.x</c> RC — it
    ///         builds, and says so once per build (NETSDK1057). That is the visible half. The invisible
    ///         half is that two people on the same repository silently get different compilers.
    ///     </para>
    ///     <para>
    ///         <c>latestFeature</c> rather than <c>latestMajor</c>: a floor that rolls forward across majors
    ///         is the behaviour there is already, and would leave both halves exactly as they are. The cost
    ///         is that a machine holding ONLY a newer major now fails outright instead of building — which
    ///         is the trade, and the message names the SDK to install.
    ///     </para>
    /// </remarks>
    public const string GlobalJson =
        $$"""
          {
            "sdk": {
              "version": "{{SdkPin}}",
              "rollForward": "latestFeature"
            }
          }

          """;
}
