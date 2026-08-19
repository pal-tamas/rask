namespace Rask.Cqrs.Client.Tests;

public sealed record GetThing(int Id) : IQuery<ThingDto>;

public sealed record ThingDto(int Id, string Name);

public sealed record RenameThing(int Id, string Name) : ICommand;

public sealed record CountThings(string Filter) : IQuery<int>;

public sealed record AttachToThing(int Id, RemoteFile File) : ICommand<string>;

public sealed record ExportThings(int Year) : IQuery<FileDownload>;

public sealed record ThingRenamed(int Id) : INotification;

// A notification WITH a client-side handler. ThingRenamed has none, so it only ever exercises the
// travelling half; this one exercises the composition — the client's own reactor still runs.
public sealed record ThingArchived(int Id) : INotification;

public sealed class ThingArchivedReactor : INotificationHandler<ThingArchived>
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<int> Recorded = [];

    public static IReadOnlyCollection<int> Seen => Recorded;

    public Task HandleAsync(ThingArchived notification, CancellationToken cancellationToken)
    {
        Recorded.Add(notification.Id);
        return Task.CompletedTask;
    }
}

// A message a client owns end to end. [LocalOnly] keeps it out of the wire vocabulary entirely, which is
// the only way a handler in a client project still gets reached - AddRaskCqrsClient replaces the invoker
// for every message that HAS a contract.
[LocalOnly]
public sealed record IncrementLocalCounter(int By) : ICommand<int>;

public sealed class IncrementLocalCounterHandler : ICommandHandler<IncrementLocalCounter, int>
{
    private static int _total;

    public Task<int> HandleAsync(IncrementLocalCounter command, CancellationToken cancellationToken) =>
        Task.FromResult(Interlocked.Add(ref _total, command.By));
}
