using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background.Tests;

// ── The jobs and the readers the #1368 tests drive: a kind anybody may run, a kind only a loader may, and one that ends as
//    its tag says — warned, refused, noted. ────────────────────────────────────────────────────────────────────────────────

// The generator matches [Authorize] by name, so the tests need no ASP.NET reference.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public string? Roles { get; set; }

    public string? Policy { get; set; }
}

/// <summary>Records its tag: a kind with no <c>[Authorize]</c>.</summary>
public sealed record OpenImport(string Tag) : IJob;

public sealed class OpenImportHandler(Recorder recorder) : ICommandHandler<OpenImport>
{
    public Task Handle(OpenImport command)
    {
        recorder.Add(command.Tag);
        return Task.CompletedTask;
    }
}

/// <summary>Records its tag: a kind only a loader may run, so only a loader sees a colleague's.</summary>
public sealed record LoaderImport(string Tag) : IJob;

[Authorize(Roles = "loader")]
public sealed class LoaderImportHandler(Recorder recorder) : ICommandHandler<LoaderImport>
{
    public Task Handle(LoaderImport command)
    {
        recorder.Add(command.Tag);
        return Task.CompletedTask;
    }
}

/// <summary>Ends as <see cref="Say" /> says: warn, refuse, note, refuse-then-throw, refuse-then-warn, note-then-warn.</summary>
public sealed record OutcomeJob(string Tag, string Say) : IJob;

public sealed class OutcomeJobHandler(Recorder recorder) : ICommandHandler<OutcomeJob>
{
    public Task Handle(OutcomeJob command)
    {
        recorder.Add(command.Tag);
        switch (command.Say)
        {
            case "warn":
                Jobs.Warn("No new rows in prices.csv");
                break;
            case "refuse":
                Jobs.Refuse("prices.csv has no header row");
                break;
            case "note":
                Jobs.Note("1,200 rows kept");
                break;
            case "refuse-throw":
                Jobs.Refuse("prices.csv has no header row");
                throw new InvalidOperationException("secret connection string");
            case "refuse-warn":
                Jobs.Refuse("prices.csv has no header row");
                Jobs.Warn("No new rows");
                break;
            case "note-warn":
                Jobs.Note("1,200 rows kept");
                Jobs.Warn("Some rows skipped");
                break;
        }

        return Task.CompletedTask;
    }
}

/// <summary>A reader of the bell: their principal, in the scope they read through, as a signed-in session's.</summary>
public sealed class SignedInReader : IPrincipalSource
{
    public ClaimsPrincipal? Current { get; set; }

    public static ClaimsPrincipal Principal(Guid user, params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.ToString()), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
        "Test",
        ClaimTypes.Name,
        ClaimTypes.Role));
}

/// <summary>Reads the board and opens the watch as one signed-in reader, in their tenant.</summary>
internal static class Readers
{
    /// <summary>A harness whose scopes know who is signed in.</summary>
    public static JobsHarness Harness(Action<JobsOptions>? configure = null) =>
        new(configure, services: services => services.AddScoped<SignedInReader>()
            .AddScoped<IPrincipalSource>(sp => sp.GetRequiredService<SignedInReader>()));

    /// <summary>What <paramref name="user" /> holding <paramref name="roles" /> sees on their board in <paramref name="tenant" />.</summary>
    public static async Task<IReadOnlyList<WorkReported>> Board(JobsHarness h, Guid user, Guid? tenant, params string[] roles)
    {
        using var scope = SignedIn(h, user, roles);
        using var where = tenant is { } t ? Tenant.Use(t) : null;
        return await scope.ServiceProvider.GetRequiredService<IWorkBoard>().Mine(lane: null, TestContext.Current.CancellationToken);
    }

    /// <summary>The watch <paramref name="user" /> opens, admitted by the real policy — or null when it refused them.</summary>
    public static async Task<WatchOwnWork?> Watch(JobsHarness h, Guid? user, Guid? tenant, params string[] roles)
    {
        using var scope = h.Get<IServiceScopeFactory>().CreateScope();
        if (user is { } who)
        {
            scope.ServiceProvider.GetRequiredService<SignedInReader>().Current = SignedInReader.Principal(who, roles);
        }

        using var where = tenant is { } t ? Tenant.Use(t) : null;
        var watch = new WatchOwnWork(Lane: null);
        var admitted = await scope.ServiceProvider.GetRequiredService<IWatchPolicy<WatchOwnWork>>()
            .CanWatch(watch, TestContext.Current.CancellationToken);
        return admitted ? watch : null;
    }

    private static IServiceScope SignedIn(JobsHarness h, Guid user, string[] roles)
    {
        var scope = h.Get<IServiceScopeFactory>().CreateScope();
        scope.ServiceProvider.GetRequiredService<SignedInReader>().Current = SignedInReader.Principal(user, roles);
        return scope;
    }
}
