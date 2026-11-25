using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

// What a save moved, as the projection saw it: each changed meter's level before and after (ProjectionTests).
public sealed record MeterLevelsMoved(IReadOnlyList<(int? Before, int? After)> Levels, IReadOnlyList<string> Tanks) : IEvent;

// A tank meter whose level and tank are projected; its label is not.
public sealed class LevelMeter : Aggregate<Guid>
{
    private LevelMeter() { } // EF materialization

    public string Tank { get; private set; } = "";

    public int Level { get; private set; }

    public string Label { get; private set; } = "";

    public static LevelMeter Fitted(string tank, int level) => new() { Id = Guid.CreateVersion7(), Tank = tank, Level = level };

    public void Read(int level) => Level = level;

    public void Move(string tank) => Tank = tank;

    public void Rename(string label) => Label = label;

    public static void Configure(EntityTypeBuilder<LevelMeter> builder) =>
        builder.Projects(
            g => new { g.Level, g.Tank },
            changed => new MeterLevelsMoved([.. changed.Select(r => (r.Before?.Level, r.After?.Level))], changed.Values(g => g.Tank)));
}
