using Microsoft.AspNetCore.Authorization;

namespace Rask.Cqrs.Transport.Tests;

// The messages both halves of the transport have to agree about. Ordinary records and ordinary
// handlers: nothing here knows it is being sent anywhere, which is the property the transport exists
// to preserve.

public sealed record Greeting(string Text, int Length, bool Formal);

public sealed record GetGreeting(string Name, bool Formal) : IQuery<Greeting>;

/// <summary>A query whose url exceeds <c>MaxQueryUrlLength</c>, so the client falls back to POST.</summary>
public sealed record CountCharacters(string Padding) : IQuery<int>;

public sealed record Bump(int By) : ICommand<int>;

public sealed record Touch(string Note) : ICommand;

public sealed record Announce(string Text) : INotification;

public sealed record Attach(string Note, RaskFile File) : ICommand<string>;

public sealed record AttachTwo(RaskFile First, RaskFile Second) : ICommand<string>;

public sealed record Export(int Year) : IQuery<FileDownload>;

public sealed record Explodes : IQuery<int>;

/// <summary>Declared, encoded, and handled nowhere — the shape of a name the server does not serve.</summary>
public sealed record Unhandled : IQuery<int>;

public sealed record Purge : ICommand;

/// <summary>The state the handlers on the far side mutate, so a test can see the dispatch landed.</summary>
public sealed class Ledger
{
    private readonly List<string> _entries = [];

    public int Count { get; private set; }

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public int Add(int by) => Count += by;

    public void Note(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }
}

public sealed class GetGreetingHandler : IQueryHandler<GetGreeting, Greeting>
{
    public Task<Greeting> HandleAsync(GetGreeting query, CancellationToken cancellationToken)
    {
        var text = query.Formal ? $"Good day, {query.Name}." : $"hi {query.Name}";
        return Task.FromResult(new Greeting(text, text.Length, query.Formal));
    }
}

public sealed class CountCharactersHandler : IQueryHandler<CountCharacters, int>
{
    public Task<int> HandleAsync(CountCharacters query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Padding.Length);
}

public sealed class BumpHandler(Ledger ledger) : ICommandHandler<Bump, int>
{
    public Task<int> HandleAsync(Bump command, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.Add(command.By));
}

public sealed class TouchHandler(Ledger ledger) : ICommandHandler<Touch>
{
    public Task HandleAsync(Touch command, CancellationToken cancellationToken)
    {
        ledger.Note($"touched:{command.Note}");
        return Task.CompletedTask;
    }
}

public sealed class AnnounceHandler(Ledger ledger) : INotificationHandler<Announce>
{
    public Task HandleAsync(Announce notification, CancellationToken cancellationToken)
    {
        ledger.Note($"announced:{notification.Text}");
        return Task.CompletedTask;
    }
}

public sealed class AttachHandler : ICommandHandler<Attach, string>
{
    public async Task<string> HandleAsync(Attach command, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            command.File.OpenReadStream(long.MaxValue, cancellationToken));

        // Name and content type as well as the bytes: a file that arrives with the right content under
        // the wrong name is the failure a handler cannot see and a user can.
        return $"{command.Note}|{command.File.Name}|{command.File.ContentType}|"
               + await reader.ReadToEndAsync(cancellationToken);
    }
}

public sealed class AttachTwoHandler : ICommandHandler<AttachTwo, string>
{
    public async Task<string> HandleAsync(AttachTwo command, CancellationToken cancellationToken)
    {
        using var first = new StreamReader(command.First.OpenReadStream(long.MaxValue, cancellationToken));
        using var second = new StreamReader(command.Second.OpenReadStream(long.MaxValue, cancellationToken));

        return $"{command.First.Name}={await first.ReadToEndAsync(cancellationToken)};"
               + $"{command.Second.Name}={await second.ReadToEndAsync(cancellationToken)}";
    }
}

public sealed class ExportHandler : IQueryHandler<Export, FileDownload>
{
    public Task<FileDownload> HandleAsync(Export query, CancellationToken cancellationToken) =>
        Task.FromResult(FileDownload.FromBytes(
            $"orders-{query.Year}.csv",
            "text/csv",
            System.Text.Encoding.UTF8.GetBytes($"id,year\n1,{query.Year}")));
}

public sealed class ExplodesHandler : IQueryHandler<Explodes, int>
{
    public Task<int> HandleAsync(Explodes query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Server=db;Password=hunter2");
}

// The attribute belongs on the HANDLER, not the message: the server reads authorization off the
// handler, because that is the side that knows what the work needs.
[Authorize(Roles = "admin")]
public sealed class PurgeHandler(Ledger ledger) : ICommandHandler<Purge>
{
    public Task HandleAsync(Purge command, CancellationToken cancellationToken)
    {
        ledger.Note("purged");
        return Task.CompletedTask;
    }
}

/// <summary>What a file input hands a component — the type a message declares, on every host.</summary>
public sealed class PickedFile(string name, string contentType, byte[] bytes) : RaskFile
{
    public override string Name => name;

    public override long Size => bytes.Length;

    public override string ContentType => contentType;

    public override DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

    public override Stream OpenReadStream(
        long maxAllowedSize = 512 * 1024,
        CancellationToken cancellationToken = default) =>
        bytes.Length > maxAllowedSize
            ? throw new IOException($"'{name}' is {bytes.Length} bytes, over the {maxAllowedSize} ceiling.")
            : new MemoryStream(bytes, writable: false);
}
