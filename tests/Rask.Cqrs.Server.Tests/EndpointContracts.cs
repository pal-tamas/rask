using Microsoft.AspNetCore.Authorization;

namespace Rask.Cqrs.Server.Tests;

public sealed record GetPublicStats : IQuery<int>;

public sealed record GetSecret(int Id) : IQuery<string>;

public sealed record DeleteThing(int Id) : ICommand;

public sealed record AdminPurge : ICommand;

public sealed record MembersOnly : ICommand;

public sealed record Explodes : IQuery<int>;

public sealed record Uploaded(string Note, RemoteFile File) : ICommand<string>;

public sealed record Export : IQuery<FileDownload>;

// No handler anywhere — so the endpoint must treat it exactly like a name it has never heard of.
public sealed record Unhandled : IQuery<int>;

[AllowAnonymous]
public sealed class GetPublicStatsHandler : IQueryHandler<GetPublicStats, int>
{
    public Task<int> HandleAsync(GetPublicStats query, CancellationToken cancellationToken) => Task.FromResult(7);
}

public sealed class GetSecretHandler : IQueryHandler<GetSecret, string>
{
    public Task<string> HandleAsync(GetSecret query, CancellationToken cancellationToken) =>
        Task.FromResult($"secret-{query.Id}");
}

public sealed class DeleteThingHandler : ICommandHandler<DeleteThing>
{
    public static int Deleted { get; set; }

    public Task HandleAsync(DeleteThing command, CancellationToken cancellationToken)
    {
        Deleted = command.Id;
        return Task.CompletedTask;
    }
}

[Authorize(Roles = "admin")]
public sealed class AdminPurgeHandler : ICommandHandler<AdminPurge>
{
    public Task HandleAsync(AdminPurge command, CancellationToken cancellationToken) => Task.CompletedTask;
}

[Authorize(Policy = "members")]
public sealed class MembersOnlyHandler : ICommandHandler<MembersOnly>
{
    public Task HandleAsync(MembersOnly command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ExplodesHandler : IQueryHandler<Explodes, int>
{
    public Task<int> HandleAsync(Explodes query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("connection string is Server=db;Password=hunter2");
}

public sealed class UploadedHandler : ICommandHandler<Uploaded, string>
{
    public async Task<string> HandleAsync(Uploaded command, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(command.File.OpenReadStream(cancellationToken));
        return $"{command.Note}:{await reader.ReadToEndAsync(cancellationToken)}";
    }
}

public sealed class ExportHandler : IQueryHandler<Export, FileDownload>
{
    public Task<FileDownload> HandleAsync(Export query, CancellationToken cancellationToken) =>
        Task.FromResult(FileDownload.FromBytes("../../etc/passwd", "text/csv", "id,name\n1,a"u8.ToArray()));
}
