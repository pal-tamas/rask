using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tax number with a pattern an empty text never matches.</summary>
public sealed partial record FirmVat(string Value) : Text(Value, MaxLength: 10, Pattern: @"^HU\d{8}$");

/// <summary>An aggregate whose old rows tell an empty text from a missing one: two texts kept empty, one not.</summary>
public sealed class Firm : Aggregate<int>, IHasForm
{
    private Firm() { }

    public FirmVat? Vat { get; private set; }

    public FirmVat? GroupVat { get; private set; }

    public string? Note { get; private set; }

    public static void Configure(EntityTypeBuilder<Firm> b)
    {
        b.KeepEmpty(f => f.Vat);
        b.KeepEmpty(f => f.Note);
    }
}
