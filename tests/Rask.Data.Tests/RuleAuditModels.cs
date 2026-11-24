using System.ComponentModel.DataAnnotations;

namespace Rask.Data.Tests;

// An aggregate with a rule of every kind `rask audit` asks of a stored row (RuleAuditTests): value objects whose
// constructor checks them, a validation attribute, Validate() over the rows, and a child with a rule of its own.
public readonly partial record struct KioskId(Guid Value);

/// <summary>A name of at most ten characters.</summary>
public sealed partial record KioskName(string Value) : Text(Value, MaxLength: 10);

/// <summary>A code kept trimmed and in capitals.</summary>
public sealed partial record KioskCode(string Value) : Text(Value, MaxLength: 8, Upper: true);

public sealed class Kiosk : Aggregate<KioskId>
{
    private readonly List<KioskTill> _tills = [];

    private Kiosk() { } // EF materialization

    public KioskName Name { get; private set; } = null!;

    public KioskCode Code { get; private set; } = null!;

    [Range(1, 8)]
    public int Lanes { get; private set; }

    public IReadOnlyCollection<KioskTill> Tills => _tills;

    public static Kiosk Open(string name, string code, int lanes) =>
        new() { Id = new KioskId(Guid.CreateVersion7()), Name = KioskName.Create(name), Code = KioskCode.Create(code), Lanes = lanes };

    public void Fit(int number) => _tills.Add(KioskTill.Numbered(number));

    private IEnumerable<Problem> Validate()
    {
        if (_tills.Count == 0)
        {
            yield return Problem.At(Tills, "A kiosk needs a till.");
        }
    }
}

public sealed class KioskTill : Entity<Guid>
{
    private KioskTill() { } // EF materialization

    public int Number { get; private set; }

    internal static KioskTill Numbered(int number) => new() { Id = Guid.CreateVersion7(), Number = number };

    private IEnumerable<Problem> Validate()
    {
        if (Number < 1)
        {
            yield return Problem.At(Number, "A till is numbered from one.");
        }
    }
}

// An aggregate whose rule is awaited: the audit asks it as a save would.
public sealed class Booth : Aggregate<Guid>
{
    private Booth() { } // EF materialization

    public int Seats { get; private set; }

    public static Booth Seating(int seats) => new() { Id = Guid.CreateVersion7(), Seats = seats };

    private async IAsyncEnumerable<Problem> Validate()
    {
        await Task.Yield();
        if (Seats > 4)
        {
            yield return Problem.At(Seats, "A booth seats four at most.");
        }
    }
}
