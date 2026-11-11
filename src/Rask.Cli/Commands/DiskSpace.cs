using System.Globalization;

namespace Rask.Cli.Commands;

/// <summary>
/// The free space on the filesystem docker builds on, and whether a deploy may build there.
///
/// <para>A deploy needs room for the build's layers and the new image, and the disk it fills is the one the apps'
/// SQLite volumes live on — a build that runs it out stops every app on the box from writing, not just this one.
/// So a deploy is refused below <see cref="Minimum"/>, and warned below <see cref="Comfortable"/> (or a fifth of a
/// small disk, so a 20 GB box is not nagged on every deploy for being small).</para>
/// </summary>
internal readonly record struct DiskSpace(long FreeBytes, long SizeBytes, string Mount)
{
    /// <summary>Below this a deploy does not start: a .NET SDK image, a restore and a publish alone come near it.</summary>
    internal const long Minimum = 3L * 1024 * 1024 * 1024;

    /// <summary>At or above this (or a fifth of the disk, when that is less) a deploy says nothing about the disk.</summary>
    internal const long Comfortable = 10L * 1024 * 1024 * 1024;

    public bool IsTooLow => FreeBytes < Minimum;

    public bool IsLow => FreeBytes < Math.Max(Minimum, Math.Min(Comfortable, SizeBytes / 5));

    /// <summary>"18.2 GB free of 368 GB on /" — the way <c>df -h</c> would put it, in docker's decimal units.</summary>
    public string Describe() => $"{Format(FreeBytes)} free of {Format(SizeBytes)} on {Mount}";

    /// <summary>A byte count in docker's own decimal units (GB = 10⁹), one decimal below 100 of a unit.</summary>
    internal static string Format(long bytes)
    {
        string[] units = ["B", "kB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        var digits = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(digits, CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
