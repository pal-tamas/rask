using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

public readonly partial record struct TallyPeriodId(int Value);

// A month of the ledger: once closed, what was written into it stays (LockedByTests).
public sealed class TallyPeriod : Aggregate<TallyPeriodId>
{
    private TallyPeriod() { } // EF materialization

    public string Name { get; private set; } = "";

    public bool Closed { get; private set; }

    public static TallyPeriod Open(string name) => new() { Name = name };

    public void Close() => Closed = true;
}

// A note written into a period, with lines of its own: a closed period refuses the note and every line of it.
public sealed class TallyNote : Aggregate<Guid>
{
    public const string PeriodClosed = "The period is closed.";

    private readonly List<TallyLine> _lines = [];

    private TallyNote() { } // EF materialization

    public TallyPeriodId Period { get; private set; }

    public string Text { get; private set; } = "";

    public IReadOnlyList<TallyLine> Lines => _lines;

    public static TallyNote Into(TallyPeriodId period, string text) => new() { Id = Guid.CreateVersion7(), Period = period, Text = text };

    public void MoveTo(TallyPeriodId period) => Period = period;

    public void Reword(string text) => Text = text;

    public TallyLine Add(int amount)
    {
        var line = TallyLine.Of(amount);
        _lines.Add(line);
        return line;
    }

    public void Remove(TallyLine line) => _lines.Remove(line);

    public static void Configure(EntityTypeBuilder<TallyNote> builder) =>
        builder.LockedBy(n => n.Period, (TallyPeriod p) => p.Closed, PeriodClosed);
}

public sealed class TallyLine : Entity<Guid>
{
    private TallyLine() { } // EF materialization

    public int Amount { get; private set; }

    public void Change(int amount) => Amount = amount;

    internal static TallyLine Of(int amount) => new() { Id = Guid.CreateVersion7(), Amount = amount };
}

// A claim that may name no period yet; its refusal names the period, and the period is read under an update lock.
public sealed class TallyClaim : Aggregate<Guid>
{
    private TallyClaim() { } // EF materialization

    public TallyPeriodId? Period { get; private set; }

    public static TallyClaim Unfiled() => new() { Id = Guid.CreateVersion7() };

    public void FileIn(TallyPeriodId? period) => Period = period;

    public static void Configure(EntityTypeBuilder<TallyClaim> builder) =>
        builder.LockedBy(c => c.Period, (TallyPeriod p) => p.Closed, p => $"{p.Name} is closed.", forUpdate: true);
}
