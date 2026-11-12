using System.Globalization;

namespace Rask.Cli.Commands;

/// <summary>
/// The name a deploy gives the release it ships: the UTC moment it started, <c>vYY.MM.DD.HH.mm.ss</c>
/// (<c>v26.11.11.13.12.02</c>). It reaches the app as <c>Rask__Version</c>, which <c>MapRaskVersion()</c> answers
/// before the assembly's own version, and rides on the image as the <see cref="Label"/>, so a rollback brings the
/// name back with the build it named.
/// </summary>
internal static class ReleaseName
{
    /// <summary>The environment variable the app reads it from — <c>Rask:Version</c> in configuration.</summary>
    internal const string Key = "Rask__Version";

    /// <summary>The image label that carries it, so a rollback restores the release with its image.</summary>
    internal const string Label = "rask.release";

    /// <summary>The release name for a deploy that started at <paramref name="moment"/>.</summary>
    internal static string At(DateTimeOffset moment) =>
        "v" + moment.UtcDateTime.ToString("yy.MM.dd.HH.mm.ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The name the user gave the release themselves (<c>--env Rask:Version=…</c>), which a deploy then leaves alone;
    /// <c>null</c> when they gave none.
    /// </summary>
    internal static string? Given(IReadOnlyList<string> env) =>
        env.LastOrDefault(e => e.StartsWith(Key + "=", StringComparison.Ordinal)) is { } entry ? entry[(Key.Length + 1)..] : null;
}
