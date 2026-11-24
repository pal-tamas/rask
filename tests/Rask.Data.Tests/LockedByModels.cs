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
        builder.LockedBy(c => c.Period, (TallyPeriod p) => p.Closed, p => $"{p.Name} is closed.", LockRead.ForUpdate);
}

// HeldTransactions' shape: a refund, filed while it has a filing not taken back, holds its transactions as filed.
public sealed class TallyRefund : Aggregate<Guid>
{
    private readonly List<TallyFiling> _filings = [];

    private TallyRefund() { } // EF materialization

    public string Name { get; private set; } = "";

    public IReadOnlyList<TallyFiling> Filings => _filings;

    public static TallyRefund Named(string name) => new() { Id = Guid.CreateVersion7(), Name = name };

    public void FileAt(string office) => _filings.Add(TallyFiling.At(office));
}

public sealed class TallyFiling : Entity<Guid>
{
    private TallyFiling() { } // EF materialization

    public string Office { get; private set; } = "";

    public DateTime? UnfiledAt { get; private set; }

    internal static TallyFiling At(string office) => new() { Id = Guid.CreateVersion7(), Office = office };
}

// A transaction a refund claims: a claim (either refund column) may take it over, nothing else may touch it while filed.
public sealed class TallyTransaction : Aggregate<Guid>
{
    private TallyTransaction() { } // EF materialization

    public Guid? RefundId { get; private set; }

    public Guid? ForeignRefundId { get; private set; }

    public decimal Litres { get; private set; }

    public static TallyTransaction Of(decimal litres) => new() { Id = Guid.CreateVersion7(), Litres = litres };

    public void ClaimFor(Guid? refund) => RefundId = refund;

    public void ClaimForeign(Guid? refund) => ForeignRefundId = refund;

    public void Measure(decimal litres) => Litres = litres;

    public static void Configure(EntityTypeBuilder<TallyTransaction> builder) =>
        builder.LockedBy(
                t => t.RefundId,
                (TallyRefund r) => r.Filings.Any(f => f.UnfiledAt == null),
                (r, write) => write.Kind == LockedWriteKind.Delete
                    ? $"{r.Name}, filed at {r.Filings[0].Office}, keeps its transactions."
                    : $"{r.Name}, filed at {r.Filings[0].Office}, holds these {write.Row.Litres} litres as filed.")
            .Except(t => t.RefundId, t => t.ForeignRefundId)
            .Including(r => r.Filings);
}
