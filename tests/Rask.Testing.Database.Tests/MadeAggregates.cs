namespace Rask.Testing.Database.Tests;

// The aggregates Test.Make<T>() makes in AggregateMakeTests: each through its own factory, referring to the others by
// a typed id (DepotId), by a plain id named after its aggregate (Guid tankId), and by the aggregate itself (Contact.At).

public readonly partial record struct DepotId(Guid Value);

/// <summary>A depot's name: a text a sample fills.</summary>
public sealed partial record DepotName(string Value) : Text(Value, MaxLength: 40);

/// <summary>A depot, keyed by a typed id.</summary>
public sealed class Depot : Aggregate<DepotId>
{
    private Depot() { } // EF materialization

    public DepotName Name { get; private set; } = null!;

    public static Depot Open(DepotName name) => new() { Id = new DepotId(Guid.CreateVersion7()), Name = name };
}

/// <summary>A tank at a depot, which it names by the depot's typed id.</summary>
public sealed class Tank : Aggregate<Guid>
{
    private Tank() { } // EF materialization

    public DepotId Depot { get; private set; }

    public Litres Capacity { get; private set; } = null!;

    public static Tank In(DepotId depot, Litres capacity) => new() { Id = Guid.CreateVersion7(), Depot = depot, Capacity = capacity };
}

/// <summary>One line of a delivery: into a tank, named by its plain id.</summary>
public sealed record DeliveryLineSpec(Guid? Id, Guid TankId, Litres Quantity);

/// <summary>A delivery to a depot, refused without a line: its factory is private, as a partial aggregate's is.</summary>
public sealed class Delivery : Aggregate<Guid>
{
    public const string NeedsALine = "A delivery needs at least one line.";

    private Delivery() { } // EF materialization

    public DepotId Depot { get; private set; }

    public Guid FirstTank { get; private set; }

    public decimal Litres { get; private set; }

    public int Lines { get; private set; }

    public string? Note { get; private set; }

    public int Copies { get; private set; }

    private static Delivery Register(DepotId depot, IReadOnlyList<DeliveryLineSpec> lines, string? note, int copies = 2)
    {
        Refuse.When(lines.Count == 0, NeedsALine);
        return new Delivery
        {
            Id = Guid.CreateVersion7(),
            Depot = depot,
            FirstTank = lines[0].TankId,
            Litres = lines.Sum(static l => l.Quantity.Value),
            Lines = lines.Count,
            Note = note,
            Copies = copies,
        };
    }
}

/// <summary>A ticket that can be made two ways, so a test names the one it means.</summary>
public sealed class Ticket : Aggregate<Guid>
{
    private Ticket() { } // EF materialization

    public string Kind { get; private set; } = "";

    public static Ticket Single(string holder) => new() { Id = Guid.CreateVersion7(), Kind = "single:" + holder };

    public static Task<Ticket> Season(string holder) => Task.FromResult(new Ticket { Id = Guid.CreateVersion7(), Kind = "season:" + holder });
}

/// <summary>A line of a memo: how much, at most a hundred.</summary>
public sealed record MemoLineSpec(Litres Quantity);

/// <summary>
///     A partial aggregate, so its private factory has a generated entry beside it and a form of its own
///     (<c>MemoWriteInput</c>) that Test.FormShows fills in.
/// </summary>
public sealed partial class Memo : Aggregate<Guid>, IVersioned
{
    public const string NeedsATitle = "A memo needs a title.";
    public const string TooMuch = "At most a hundred litres a line.";
    public const string CannotGrow = "A memo's litres only go down.";

    private Memo() { } // EF materialization

    public string Title { get; private set; } = "";

    public decimal Litres { get; private set; }

    private static Memo Write(string title, IReadOnlyList<MemoLineSpec> lines)
    {
        Refuse.When(string.IsNullOrWhiteSpace(title), () => title, NeedsATitle);
        for (var i = 0; i < lines.Count; i++)
        {
            Refuse.When(lines[i].Quantity.Value > 100m, () => lines[i].Quantity, TooMuch);
        }

        return new Memo { Id = Guid.CreateVersion7(), Title = title, Litres = lines.Sum(static l => l.Quantity.Value) };
    }

    /// <summary>Changes the title and the litres, which may only shrink: an operation whose form starts from what is stored.</summary>
    public void Amend(string title, decimal litres)
    {
        Refuse.When(string.IsNullOrWhiteSpace(title), () => title, NeedsATitle);
        Refuse.When(litres > Litres, () => litres, CannotGrow);
        Title = title;
        Litres = litres;
    }
}
