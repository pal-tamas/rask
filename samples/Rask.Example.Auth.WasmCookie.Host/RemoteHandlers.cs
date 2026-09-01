using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Rask.Cqrs;

namespace Rask.Example.Auth.WasmCookie.Host;

// The server half of the messages the bundle dispatches. They are ordinary handlers — nothing here
// mentions HTTP, a route, or the fact that the caller is in a browser. The one thing they do use is
// the request's own principal, which is what makes the answer proof that the cookie travelled.
//
// Compiled only here. The bundle sees the message records (linked into this project) and never these,
// so a credential or a rule in a handler cannot reach a download anybody can read.

/// <summary>Counts the visits noted, for the lifetime of the process.</summary>
public sealed class VisitCounter
{
    private int _count;

    public int Note() => Interlocked.Increment(ref _count);
}

public sealed class WhoAmIHandler(IHttpContextAccessor accessor) : IQueryHandler<WhoAmI, ServerIdentity>
{
    public Task<ServerIdentity> HandleAsync(WhoAmI query, CancellationToken cancellationToken)
    {
        var user = accessor.HttpContext?.User;
        return Task.FromResult(new ServerIdentity(
            user?.Identity?.Name ?? "nobody",
            user?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? []));
    }
}

public sealed class NoteVisitHandler(VisitCounter counter) : ICommandHandler<NoteVisit, int>
{
    public Task<int> HandleAsync(NoteVisit command, CancellationToken cancellationToken) =>
        Task.FromResult(counter.Note());
}
