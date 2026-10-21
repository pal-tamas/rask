using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
/// A job runs signed in as the user who enqueued it, with the rights they had THEN and still have NOW — never one
/// they gained since, never one they lost since, and nothing at all once their account is gone.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobRightsTests
{
    [Fact]
    public async Task A_job_keeps_only_the_roles_its_user_had_at_enqueue_and_still_has()
    {
        var user = Guid.NewGuid();
        var accounts = new Accounts { [user] = Principal(user, "admin", "viewer") };
        await using var h = Harness(accounts);

        await Enqueue(h, Principal(user, "admin", "editor"), new RightsJob("mine"));
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal($"mine:{user}:admin", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task A_role_granted_after_the_enqueue_is_not_the_jobs()
    {
        var user = Guid.NewGuid();
        var accounts = new Accounts { [user] = Principal(user, "admin") };
        await using var h = Harness(accounts);

        await Enqueue(h, Principal(user), new RightsJob("granted"));
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal($"granted:{user}:", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task A_role_revoked_after_the_enqueue_is_gone_from_the_job_too()
    {
        var user = Guid.NewGuid();
        var accounts = new Accounts { [user] = Principal(user) };
        await using var h = Harness(accounts);

        await Enqueue(h, Principal(user, "admin"), new RightsJob("revoked"));
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal($"revoked:{user}:", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task A_job_whose_users_account_is_gone_runs_signed_in_as_nobody()
    {
        var user = Guid.NewGuid();
        await using var h = Harness(new Accounts());

        await Enqueue(h, Principal(user, "admin"), new RightsJob("gone"));
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal("gone:signed-out:", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task Without_accounts_to_ask_a_job_runs_with_the_rights_recorded_at_enqueue_alone()
    {
        var user = Guid.NewGuid();
        await using var h = Harness(accounts: null);

        await Enqueue(h, Principal(user, "admin"), new RightsJob("recorded"));
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal($"recorded:{user}:admin", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task A_job_enqueued_for_another_user_never_carries_the_signed_in_users_rights()
    {
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        var accounts = new Accounts { [other] = Principal(other, "admin") };
        await using var h = Harness(accounts);

        using (Current.UseUser(other))
        {
            await Enqueue(h, Principal(admin, "admin"), new RightsJob("other"));
        }

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal($"other:{other}:", Assert.Single(h.Recorder.Values));
    }

    [Fact]
    public async Task The_row_keeps_the_rights_and_never_who_the_user_is()
    {
        var user = Guid.NewGuid();
        await using var h = Harness(new Accounts());

        await Enqueue(h, Principal(user, "admin"), new RightsJob("row"));

        var job = await h.SingleJobAsync();
        Assert.Equal("""[["http://schemas.microsoft.com/ws/2008/06/identity/claims/role","admin"]]""", job.UserClaims);
    }

    [Fact]
    public async Task A_database_without_the_user_claims_column_says_which_commands_to_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-noclaims-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Job DROP COLUMN UserClaims;", cancellationToken: TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Job (Type, Payload, RunAt, Attempts, CreatedAt, UpdatedAt)
                VALUES ('x', 'null', '2020-01-01 00:00:00', 0, '2020-01-01 00:00:00', '2020-01-01 00:00:00');
                """, cancellationToken: TestContext.Current.CancellationToken);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(
                h.Logs.Any(l => l.Contains("rask db add AddJobUserClaims", StringComparison.Ordinal))));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(h.Logs, l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }

    private static JobsHarness Harness(Accounts? accounts) => new(services: services =>
    {
        if (accounts is not null)
        {
            services.AddSingleton<IAccountPrincipals>(accounts);
        }

        services.AddScoped<SignedIn>();
        services.AddScoped<IScopeUser>(sp => sp.GetRequiredService<SignedIn>());
        services.AddScoped<IPrincipalSource>(sp => sp.GetRequiredService<SignedIn>());
    });

    // The enqueuing page: its user signed in, read through the scope as Current.Principal is.
    private static async Task Enqueue(JobsHarness h, ClaimsPrincipal user, IJob job)
    {
        var page = new ServiceCollection().AddSingleton<IPrincipalSource>(new SignedIn { Current = user }).BuildServiceProvider();
        using (Db.UseScope(page))
        {
            await h.Queue.Enqueue(job, TestContext.Current.CancellationToken);
        }
    }

    private static ClaimsPrincipal Principal(Guid user, params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.Email, "u@example.com"),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
        "Test",
        ClaimTypes.Name,
        ClaimTypes.Role));

    private sealed class Accounts : Dictionary<Guid, ClaimsPrincipal>, IAccountPrincipals
    {
        public Task<ClaimsPrincipal?> Of(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(TryGetValue(userId, out var principal) ? principal : null);
    }

    private sealed class SignedIn : IScopeUser, IPrincipalSource
    {
        public ClaimsPrincipal? Current { get; set; }

        public void SignIn(ClaimsPrincipal user) => Current = user;
    }
}

public sealed record RightsJob(string Tag) : IJob;

public sealed class RightsJobHandler(Recorder recorder) : ICommandHandler<RightsJob>
{
    public Task Handle(RightsJob command)
    {
        var principal = Current.Principal;
        var who = principal?.Identity?.IsAuthenticated == true ? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value : "signed-out";
        var roles = string.Join(',', (principal?.FindAll(ClaimTypes.Role) ?? []).Select(role => role.Value).Order(StringComparer.Ordinal));
        recorder.Add($"{command.Tag}:{who}:{roles}");
        return Task.CompletedTask;
    }
}
