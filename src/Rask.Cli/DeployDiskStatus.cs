namespace Rask.Cli;

/// <summary>
///     The <c>disk</c> of <c>rask deploy status --json</c>: the box's free space where docker keeps its images and
///     build cache, whether the app's guard would warn (<see cref="IsLow"/>) or refuse a deploy (<see cref="IsTooLow"/>)
///     there, and what <c>docker system df</c> says could be reclaimed, as docker writes it (<c>98.2GB (81%)</c>).
///     A part that could not be read is absent rather than guessed.
/// </summary>
internal sealed record DeployDiskStatus(
    string? Mount,
    long? FreeBytes,
    long? SizeBytes,
    bool IsLow,
    bool IsTooLow,
    string? ReclaimableImages,
    string? ReclaimableBuildCache);
