using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

// A firkin on a shelf, labelled once: what the writes over many rows load, stamp, match and import (BulkWritesTests).
public sealed class Firkin : Aggregate<int>, IHasTimestamps, IVersioned, IDeletable
{
    public const string Negative = "A firkin cannot hold fewer than none.";

    private Firkin() { } // EF materialization

    public string Shelf { get; private set; } = "";

    public string Label { get; private set; } = "";

    public int Units { get; private set; }

    public static Firkin On(string shelf, string label, int count) => new() { Shelf = shelf, Label = label, Units = count };

    public void Restock(int count) => Units = count;

    public void MoveTo(string shelf) => Shelf = shelf;

    private IEnumerable<Problem> Validate()
    {
        if (Units < 0)
        {
            yield return Problem.At(Units, Negative);
        }
    }
}

// A slip filed into a ledger period: a closed period keeps its slips, whatever writes them.
public sealed class FirkinSlip : Aggregate<Guid>, IDeletable
{
    private FirkinSlip() { } // EF materialization

    public TallyPeriodId Period { get; private set; }

    public string Text { get; private set; } = "";

    public static FirkinSlip Into(TallyPeriodId period, string text) => new() { Id = Guid.CreateVersion7(), Period = period, Text = text };

    public static void Configure(EntityTypeBuilder<FirkinSlip> builder) =>
        builder.LockedBy(s => s.Period, (TallyPeriod p) => p.Closed, TallyNote.PeriodClosed);
}

// A line a firkin import reads: a label, a shelf and a count, as a file gives them.
public sealed record FirkinLine(string Shelf, string Label, int Count);
