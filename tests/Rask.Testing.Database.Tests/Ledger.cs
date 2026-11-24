using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>A ledger, which says when it is opened and when it is renamed.</summary>
public sealed class Ledger : Aggregate<Guid>
{
    private Ledger() { } // EF materialization

    public string Name { get; private set; } = "";

    public static Ledger Open(string name)
    {
        var ledger = new Ledger { Id = Guid.NewGuid(), Name = name };
        ledger.Raise(new LedgerOpened(ledger.Id, name));
        return ledger;
    }

    public void Rename(string name)
    {
        Name = name;
        Raise(new LedgerRenamed(Id, name));
    }
}

/// <summary>A line written into a ledger, by the handler of its opening rather than by the test.</summary>
public sealed class LedgerLine : Aggregate<Guid>
{
    private LedgerLine() { } // EF materialization

    public Guid LedgerId { get; private set; }

    public string Text { get; private set; } = "";

    public static LedgerLine In(Guid ledger, string text) => new() { Id = Guid.NewGuid(), LedgerId = ledger, Text = text };
}

public sealed record LedgerOpened(Guid Ledger, string Name) : IEvent;

public sealed record LedgerRenamed(Guid Ledger, string Name) : IEvent;

/// <summary>Writes the opening line through a context of the app's, as a handler in an app does.</summary>
public sealed class WriteOpeningLine(IDbContextFactory<PumpsContext> contexts) : IEventHandler<LedgerOpened>
{
    public async Task Handle(LedgerOpened e)
    {
        await using var db = await contexts.CreateDbContextAsync();
        db.Add(LedgerLine.In(e.Ledger, $"Opened {e.Name}"));
        await db.SaveChangesAsync();
    }
}

/// <summary>What the renames handler heard — a service the test registers, as an app registers its own.</summary>
public sealed class Renames
{
    public List<string> Heard { get; } = [];
}

public sealed class NoteRename(Renames renames) : IEventHandler<LedgerRenamed>
{
    public Task Handle(LedgerRenamed e)
    {
        renames.Heard.Add(e.Name);
        return Task.CompletedTask;
    }
}
