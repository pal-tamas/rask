using System.Globalization;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     The .NET version a scaffolded project targets, and everything in the written files that names it.
/// </summary>
/// <remarks>
///     <para>
///         The committed template trees are written against the DEFAULT target, byte for byte — that is the
///         whole point of <see cref="TemplateMaterializer" />, where the tree IS the output. Choosing another
///         version therefore rewrites the few places a framework appears on the way out, and rewrites them by
///         exact literal: a csproj's <c>&lt;TargetFramework&gt;</c>, the two Docker image tags, and the build
///         output path in <c>.vscode/launch.json</c>. The one place it does NOT rewrite is
///         <c>global.json</c>, which is generated from <see cref="GlobalJson" /> already naming the right
///         SDK band — see <see cref="SdkPin" /> for why that file cannot name a real SDK version.
///     </para>
///     <para>
///         Rask's own packages ship for every version here, so the choice only decides what the app targets.
///         The LTS release stays the default: an app inherits its support window from the runtime it names.
///     </para>
/// </remarks>
internal sealed record DotnetTarget(string Moniker, string BrowserMoniker, string DockerTag)
{
    /// <summary>.NET 10 — LTS, supported to November 2028, and what `rask new` scaffolds unless told otherwise.</summary>
    public static DotnetTarget Default { get; } = new("net10.0", "net10.0-browser", "10.0");

    /// <summary>.NET 11 — STS. Needs the .NET 11 SDK; the app is otherwise identical.</summary>
    public static DotnetTarget Preview { get; } = new("net11.0", "net11.0-browser", "11.0");

    /// <summary>The accepted <c>--framework</c> values, in the order `--help` should list them.</summary>
    public static IReadOnlyList<string> Monikers { get; } = [Default.Moniker, Preview.Moniker];

    /// <summary>The major version an SDK must have to build this target.</summary>
    public int SdkMajor => int.Parse(Moniker["net".Length..].Split('.')[0], CultureInfo.InvariantCulture);

    /// <summary>The version a scaffold's <c>global.json</c> names, as <c>{major}.0.0</c>.</summary>
    /// <remarks>
    ///     The band floor rather than a real SDK version, and that is load-bearing in both directions.
    ///     Naming a shipped version — <c>11.0.100</c> — resolves NOTHING while .NET 11 is still a release
    ///     candidate, because <c>11.0.100-rc.1</c> sorts BELOW <c>11.0.100</c> and roll-forward only ever
    ///     goes up; <c>allowPrerelease</c> does not change that, and the scaffold fails with "could not be
    ///     loaded" before a line of it is compiled. Naming a real installed version instead would pin the
    ///     output to whatever happened to be on the scaffolding machine, which is not something a
    ///     committed file should say. <c>{major}.0.0</c> matches the earliest possible SDK in the band, so
    ///     every later one — prerelease or not — rolls forward onto it, and it never goes stale at GA.
    /// </remarks>
    public string SdkPin => $"{SdkMajor}.0.0";

    /// <summary>
    ///     The <c>global.json</c> every scaffold writes: the app is built by an SDK of its OWN major.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this file the SDK picks the newest one installed, which on a machine that also has
    ///         the next major in preview means a <c>net10.0</c> app is compiled by an <c>11.0.x</c> RC — it
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
    public string GlobalJson =>
        $$"""
          {
            "sdk": {
              "version": "{{SdkPin}}",
              "rollForward": "latestFeature"
            }
          }

          """;

    /// <summary>The target for a <c>--framework</c> value the parser has already validated.</summary>
    public static DotnetTarget For(string? moniker) =>
        string.Equals(moniker, Preview.Moniker, StringComparison.Ordinal) ? Preview : Default;

    /// <summary>
    ///     Rewrites every framework the templates name, in one file's text.
    /// </summary>
    /// <remarks>
    ///     Exact literals rather than a pattern, and the caller checks that something was rewritten in each
    ///     csproj and Dockerfile: a silent miss would scaffold a project whose csproj and container disagree
    ///     about the .NET version, which fails at `docker build` time with a restore error naming neither.
    /// </remarks>
    public string Rewrite(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this == Default
            ? text
            : text
                .Replace(
                    $"<TargetFramework>{Default.BrowserMoniker}</TargetFramework>",
                    $"<TargetFramework>{BrowserMoniker}</TargetFramework>",
                    StringComparison.Ordinal)
                .Replace(
                    $"<TargetFramework>{Default.Moniker}</TargetFramework>",
                    $"<TargetFramework>{Moniker}</TargetFramework>",
                    StringComparison.Ordinal)
                .Replace($"dotnet/sdk:{Default.DockerTag}", $"dotnet/sdk:{DockerTag}", StringComparison.Ordinal)
                .Replace($"dotnet/aspnet:{Default.DockerTag}", $"dotnet/aspnet:{DockerTag}", StringComparison.Ordinal)
                // The build output path, which .vscode/launch.json names to find the dll it starts. Miss it
                // and F5 fails with "program does not exist" on a project that builds perfectly.
                .Replace($"bin/Debug/{Default.Moniker}/", $"bin/Debug/{Moniker}/", StringComparison.Ordinal)
                // The global.json pin. TemplateMaterializer writes that file from GlobalJson, already
                // correct, so this reaches it only if a template tree ever commits one of its own — which
                // would otherwise pin a --framework net11.0 scaffold to a 10.0.x SDK and fail its restore.
                .Replace(
                    $"\"version\": \"{Default.SdkPin}\"",
                    $"\"version\": \"{SdkPin}\"",
                    StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="text" /> still names the default framework after a rewrite.</summary>
    public bool StillNamesTheDefault(string text) =>
        text is not null
        && (text.Contains($"<TargetFramework>{Default.Moniker}<", StringComparison.Ordinal)
            || text.Contains($"<TargetFramework>{Default.BrowserMoniker}<", StringComparison.Ordinal)
            || text.Contains($"dotnet/sdk:{Default.DockerTag}", StringComparison.Ordinal)
            || text.Contains($"dotnet/aspnet:{Default.DockerTag}", StringComparison.Ordinal)
            || text.Contains($"bin/Debug/{Default.Moniker}/", StringComparison.Ordinal)
            || text.Contains($"\"version\": \"{Default.SdkPin}\"", StringComparison.Ordinal));
}
