using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// How a deploy treats the box's disk, from <c>.rask/deploy.json</c>: the build cache a clean-up keeps
/// (<c>"buildCacheKeep"</c>), the free space below which a deploy is refused (<c>"minFreeDisk"</c>), and below which
/// it is warned (<c>"warnFreeDisk"</c>). Each is absent from the file unless an app turns it.
///
/// <para>A deploy needs room for the build's layers and the new image, and the disk it fills is the one the apps'
/// SQLite volumes live on — a build that runs it out stops every app on the box from writing, not just this one.
/// Unless <c>"warnFreeDisk"</c> says otherwise, the warning comes below <see cref="Comfortable"/> or a fifth of a
/// small disk, whichever is less, so a 20 GB box is not nagged on every deploy for being small.</para>
/// </summary>
internal sealed record DiskPolicy(long BuildCacheKeep, long MinFree, long? WarnFree)
{
    internal const string BuildCacheKeepKey = "buildCacheKeep";

    internal const string MinFreeKey = "minFreeDisk";

    internal const string WarnFreeKey = "warnFreeDisk";

    private const long Gibibyte = 1024L * 1024 * 1024;

    /// <summary>The build cache a clean-up leaves on the box: room for a few apps' SDK, restore and publish layers.</summary>
    internal const long DefaultBuildCacheKeep = 20 * Gibibyte;

    /// <summary>Below this a deploy does not start: a .NET SDK image, a restore and a publish alone come near it.</summary>
    internal const long DefaultMinFree = 3 * Gibibyte;

    /// <summary>At or above this (or a fifth of the disk, when that is less) a deploy says nothing about the disk.</summary>
    internal const long Comfortable = 10 * Gibibyte;

    internal static DiskPolicy Default { get; } = new(DefaultBuildCacheKeep, DefaultMinFree, null);

    /// <summary>The settings as the app wrote them, carried back into the file a deploy saves.</summary>
    private (string? Keep, string? MinFree, string? WarnFree) Written { get; init; }

    /// <summary>The build cache to keep as the prune's argument (<c>20GB</c>), which docker reads as this many bytes.</summary>
    public string BuildCacheKeepArgument => ByteSize.Format(BuildCacheKeep);

    public bool IsTooLow(DiskSpace disk) => disk.FreeBytes < MinFree;

    public bool IsLow(DiskSpace disk) =>
        disk.FreeBytes < Math.Max(MinFree, WarnFree ?? Math.Min(Comfortable, disk.SizeBytes / 5));

    /// <summary>What the warning threshold is, said in a dry run: a size, or the default rule.</summary>
    public string DescribeWarn() =>
        WarnFree is { } warn ? DiskSpace.Format(warn) : $"{DiskSpace.Format(Comfortable)} or a fifth of the disk";

    /// <summary>The app's settings, or what is wrong with them (<paramref name="error"/>, naming the key).</summary>
    internal static bool TryFrom(DeployConfig config, out DiskPolicy policy, out string? error)
    {
        policy = Default;
        long keep = DefaultBuildCacheKeep, minFree = DefaultMinFree, warnFree = 0;
        if ((config.BuildCacheKeep is { } keepText && !ByteSize.TryParse(BuildCacheKeepKey, keepText, out keep, out error))
            || (config.MinFreeDisk is { } minText && !ByteSize.TryParse(MinFreeKey, minText, out minFree, out error))
            || (config.WarnFreeDisk is { } warnText && !ByteSize.TryParse(WarnFreeKey, warnText, out warnFree, out error)))
        {
            return false;
        }

        if (config.WarnFreeDisk is not null && warnFree < minFree)
        {
            error = $"\"{WarnFreeKey}\": \"{config.WarnFreeDisk}\" is below \"{MinFreeKey}\" ({ByteSize.Format(minFree)}) — a deploy would be refused before it was ever warned. Make it at least as large.";
            return false;
        }

        policy = new DiskPolicy(keep, minFree, config.WarnFreeDisk is null ? null : warnFree)
        {
            Written = (config.BuildCacheKeep, config.MinFreeDisk, config.WarnFreeDisk),
        };
        error = null;
        return true;
    }

    /// <summary>Write the settings back into <paramref name="config"/> as the app wrote them — absent when it set none.</summary>
    internal void Remember(DeployConfig config)
    {
        config.BuildCacheKeep = Written.Keep;
        config.MinFreeDisk = Written.MinFree;
        config.WarnFreeDisk = Written.WarnFree;
    }
}
