using System.Security.Claims;

namespace Rask.Cqrs.Tests;

// The generator matches [Authorize] and [AllowAnonymous] by name, so Rask.Cqrs needs no reference to ASP.NET —
// and neither does this project.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public string? Roles { get; set; }

    public string? Policy { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousAttribute : Attribute;

public sealed record DeleteEverything : ICommand;

[Authorize(Roles = "admin, owner")]
public sealed class DeleteEverythingHandler(Recorder recorder) : ICommandHandler<DeleteEverything>
{
    public Task Handle(DeleteEverything command)
    {
        recorder.Add("deleted");
        return Task.CompletedTask;
    }
}

public sealed record CloseBooks : ICommand;

[Authorize(Roles = "admin")]
[Authorize(Roles = "finance")]
public sealed class CloseBooksHandler(Recorder recorder) : ICommandHandler<CloseBooks>
{
    public Task Handle(CloseBooks command)
    {
        recorder.Add("closed");
        return Task.CompletedTask;
    }
}

public sealed record SaveDraft : ICommand;

[Authorize]
public sealed class SaveDraftHandler(Recorder recorder) : ICommandHandler<SaveDraft>
{
    public Task Handle(SaveDraft command)
    {
        recorder.Add("saved");
        return Task.CompletedTask;
    }
}

public sealed record GetSalaries : IQuery<int>;

[Authorize(Roles = "admin")]
public sealed class GetSalariesHandler : IQueryHandler<GetSalaries, int>
{
    public Task<int> Handle(GetSalaries query) => Task.FromResult(42);
}

public sealed record ReadTerms : IQuery<string>;

[Authorize(Roles = "admin")]
[AllowAnonymous]
public sealed class ReadTermsHandler : IQueryHandler<ReadTerms, string>
{
    public Task<string> Handle(ReadTerms query) => Task.FromResult("terms");
}

public sealed record ShipOrder : ICommand;

[Authorize(Policy = "CanShip")]
public sealed class ShipOrderHandler(Recorder recorder) : ICommandHandler<ShipOrder>
{
    public Task Handle(ShipOrder command)
    {
        recorder.Add("shipped");
        return Task.CompletedTask;
    }
}

// Who the work in flight is for, as a host would say it. Null is a job or a hosted service: nobody.
public sealed class FixedPrincipal(ClaimsPrincipal? current) : IDispatchPrincipal
{
    public ClaimsPrincipal? Current { get; } = current;
}

public sealed class FixedPolicies(bool permits) : IPolicyEvaluator
{
    public Task<bool> Permits(ClaimsPrincipal user, string policy) => Task.FromResult(permits);
}
