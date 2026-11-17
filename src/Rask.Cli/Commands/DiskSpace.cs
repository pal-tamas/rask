using System.Globalization;

namespace Rask.Cli.Commands;

/// <summary>
/// The free space on the filesystem docker builds on, as <see cref="HostDisk"/> read it. Whether a deploy may build
/// there is the app's <see cref="DiskPolicy"/> to say.
/// </summary>
internal readonly record struct DiskSpace(long FreeBytes, long SizeBytes, string Mount)
{
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
