using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

// A partial aggregate whose public methods are operations and whose non-public factory is born through its generated
// entry; sealing it takes a captain or an admiral, planning one an admiral (OperationEntryTests).
public sealed partial class Expedition : Aggregate<Guid, Versioned, Deletable>
{
    private Expedition() { } // EF materialization

    [Required, StringLength(10)]
    public string Title { get; private set; } = "";

    [Range(1, 100)]
    public int Seats { get; private set; }

    public bool Sealed { get; private set; }

    public void Book(int seats) => Seats = seats;

    public void Seal() => Sealed = true;

    public async Task Rename(string title)
    {
        await Task.Yield();
        Refuse.When(Sealed, () => Title, "A sealed expedition keeps its name.");
        Title = title;
    }

    // Every save takes the expeditions' lock without waiting, so a test that holds it sees whether a run took it.
    private async IAsyncEnumerable<Problem> Validate()
    {
        await using var turn = await Db.Lock(Lock).For(TimeSpan.Zero);
        if (Sealed && Seats > 50)
        {
            yield return Problem.At(Seats, "A sealed expedition takes at most 50.");
        }
    }

    internal const string Lock = "expeditions";

    // A sealed expedition is kept: what its delete, and the editor's Delete before it asks, refuse.
    private IEnumerable<Problem> ValidateDelete()
    {
        if (Sealed)
        {
            yield return Problem.Whole("A sealed expedition is kept.");
        }
    }

    public static void Configure(EntityTypeBuilder<Expedition> b)
    {
        b.Operation(e => e.Seal, roles: "Captain,Admiral");
        b.Operation(nameof(Plan), roles: "Admiral");
        b.Operation(nameof(Delete), roles: "Admiral");
    }

    private static Expedition Plan(string title, int seats) => new() { Id = Guid.CreateVersion7(), Title = title, Seats = seats };

    // The tests' own way to a stored expedition, past the factory's roles.
    internal static Expedition Planned(string title, int seats) => Plan(title, seats);
}
