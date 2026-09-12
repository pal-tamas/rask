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
///         exact literal: a csproj's <c>&lt;TargetFramework&gt;</c> and the two Docker image tags.
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
                .Replace($"bin/Debug/{Default.Moniker}/", $"bin/Debug/{Moniker}/", StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="text" /> still names the default framework after a rewrite.</summary>
    public bool StillNamesTheDefault(string text) =>
        text is not null
        && (text.Contains($"<TargetFramework>{Default.Moniker}<", StringComparison.Ordinal)
            || text.Contains($"<TargetFramework>{Default.BrowserMoniker}<", StringComparison.Ordinal)
            || text.Contains($"dotnet/sdk:{Default.DockerTag}", StringComparison.Ordinal)
            || text.Contains($"dotnet/aspnet:{Default.DockerTag}", StringComparison.Ordinal)
            || text.Contains($"bin/Debug/{Default.Moniker}/", StringComparison.Ordinal));
}
