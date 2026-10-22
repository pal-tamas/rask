using System.Globalization;

namespace Rask.Cli.Commands;

/// <summary>
/// How long each step of a deploy may take before it is given up on: a stalled step fails, named, instead of holding
/// the deploy (and its lock) for as long as the connection stays open. Only the old colour's drain is meant to be long,
/// and <c>stopTimeout</c> bounds it; a build is bounded twice — in all (<see cref="Build"/>) and by how long it may
/// print nothing (<see cref="BuildIdle"/>), which is what a stalled download inside it looks like.
/// </summary>
internal sealed record DeployDeadlines(TimeSpan Step, TimeSpan Build, TimeSpan BuildIdle, TimeSpan Progress)
{
    /// <summary>The <c>.rask/deploy.json</c> key for <see cref="Build"/>.</summary>
    internal const string BuildKey = "buildTimeout";

    /// <summary>The <c>.rask/deploy.json</c> key for <see cref="BuildIdle"/>.</summary>
    internal const string BuildIdleKey = "buildIdleTimeout";

    internal static readonly TimeSpan DefaultBuild = TimeSpan.FromMinutes(45);

    internal static readonly TimeSpan DefaultBuildIdle = TimeSpan.FromMinutes(15);

    internal static readonly TimeSpan MinimumBuild = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan MaximumBuild = TimeSpan.FromHours(6);

    /// <summary>Every other docker call — a tag, a run, an inspect, a probe, a proxy reload — answers in seconds.</summary>
    internal static DeployDeadlines Default { get; } = new(TimeSpan.FromMinutes(5), DefaultBuild, DefaultBuildIdle, TimeSpan.FromMinutes(1));

    /// <summary>
    /// The deadline for one docker call, or <c>null</c> for none: <c>logs --follow</c> runs until the user stops it,
    /// and <c>stop -t N</c> waits out the app's own stop budget, so it gets that on top of a step.
    /// </summary>
    internal TimeSpan? For(IReadOnlyList<string> arguments) =>
        Verb(arguments) switch
        {
            "build" => Build,
            "logs" when arguments.Contains("--follow", StringComparer.Ordinal) || arguments.Contains("-f", StringComparer.Ordinal) => null,
            "stop" when StopSeconds(arguments) is { } seconds => Step + TimeSpan.FromSeconds(seconds),
            _ => Step,
        };

    /// <summary>The docker subcommand, past the <c>-H ssh://…</c> a deploy puts in front of every remote call.</summary>
    internal static string Verb(IReadOnlyList<string> arguments)
    {
        var at = arguments.Count > 1 && string.Equals(arguments[0], "-H", StringComparison.Ordinal) ? 2 : 0;
        return arguments.Count > at ? arguments[at] : string.Empty;
    }

    private static int? StopSeconds(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i + 1 < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], "-t", StringComparison.Ordinal)
                && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return seconds;
            }
        }

        return null;
    }
}
