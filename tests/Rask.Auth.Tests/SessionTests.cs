using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Auth.Tests;

/// <summary>Session rows: the thing a sign-in cookie points at, and what ending a session deletes.</summary>
[Collection(AuthDbCollection.Name)]
public sealed class SessionTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";

    [Fact]
    public async Task A_started_session_resumes_as_its_user_with_its_roles_and_its_id()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;

        var sessionId = await Sessions(harness).StartAsync(owner.Id, "10.0.0.1", "test-agent", persistent: true, cancellationToken: TestContext.Current.CancellationToken);
        var principal = await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(principal);
        Assert.Equal(owner.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(Owner, principal.Identity!.Name);
        Assert.True(principal.IsInRole(RaskRoles.Admin));
        Assert.Equal(sessionId.ToString(), principal.FindFirstValue("sid"));

        await using var db = harness.NewContext();
        var row = await db.Set<Session>().SingleAsync(s => s.Id == sessionId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("10.0.0.1", row.IpAddress);
        Assert.Equal("test-agent", row.UserAgent);
        Assert.True(row.Persistent);
    }

    [Fact]
    public async Task An_ended_session_does_not_resume()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false, cancellationToken: TestContext.Current.CancellationToken);

        await Sessions(harness).EndAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Null(await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken));

        // Soft-deleted, like every aggregate: the row is still there for a device list to say "signed out".
        await using var db = harness.NewContext();
        Assert.NotNull(await db.Set<Session>().IgnoreQueryFilters().SingleAsync(s => s.Id == sessionId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ending_every_other_session_keeps_the_one_asking()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessions = Sessions(harness);

        var here = await sessions.StartAsync(owner.Id, null, "laptop", false, TestContext.Current.CancellationToken);
        var phone = await sessions.StartAsync(owner.Id, null, "phone", false, TestContext.Current.CancellationToken);
        var tablet = await sessions.StartAsync(owner.Id, null, "tablet", false, TestContext.Current.CancellationToken);

        Assert.Equal(2, await sessions.EndAllAsync(owner.Id, except: here, cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(await sessions.ResumeAsync(here, TestContext.Current.CancellationToken));
        Assert.Null(await sessions.ResumeAsync(phone, TestContext.Current.CancellationToken));
        Assert.Null(await sessions.ResumeAsync(tablet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_session_left_unused_past_its_lifetime_does_not_resume()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        await using var harness = await ClaimedAsync(o => o.ExpireTimeSpan = TimeSpan.FromHours(1), clock);
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false, TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_session_in_use_slides_its_expiry_forward()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        await using var harness = await ClaimedAsync(o => o.ExpireTimeSpan = TimeSpan.FromHours(1), clock);
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false, TestContext.Current.CancellationToken);

        // Used every 50 minutes for three hours: each use lands inside the hour the last one granted.
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(50));
            Assert.NotNull(await ResumeUncachedAsync(harness, sessionId));
        }
    }

    [Fact]
    public async Task A_deleted_user_s_sessions_do_not_resume()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false, TestContext.Current.CancellationToken);

        await using (var db = harness.NewContext())
        {
            db.Remove(await db.Set<TestUser>().SingleAsync(u => u.Id == owner.Id, cancellationToken: TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Null(await ResumeUncachedAsync(harness, sessionId));
    }

    [Fact]
    public async Task The_sweep_keeps_an_ended_session_for_a_day()
    {
        // The sweep also runs as the host starts. It used to delete every ended row at once, so one that ran late
        // removed a session a device list was still meant to show as signed out.
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var harness = await ClaimedAsync(clock: clock);
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false, cancellationToken: TestContext.Current.CancellationToken);
        await Sessions(harness).EndAsync(sessionId, TestContext.Current.CancellationToken);
        var sweep = harness.Services.GetServices<IHostedService>().OfType<SessionSweep<AuthDbContext>>().Single();

        await sweep.SweepAsync(CancellationToken.None);
        Assert.True(await RowExistsAsync(harness, sessionId));

        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await sweep.SweepAsync(CancellationToken.None);
        Assert.False(await RowExistsAsync(harness, sessionId));
    }

    private static async Task<bool> RowExistsAsync(AuthHarness harness, Guid sessionId)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().AnyAsync(s => s.Id == sessionId);
    }

    // The resume cache would otherwise answer from the first read for 30 seconds of wall-clock time.
    private static Task<ClaimsPrincipal?> ResumeUncachedAsync(AuthHarness harness, Guid sessionId)
    {
        harness.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Remove("Rask.Auth.Session:" + sessionId.ToString("N"));
        return Sessions(harness).ResumeAsync(sessionId);
    }

    [Fact]
    public async Task A_claim_the_user_gives_for_itself_is_on_the_principal_a_sign_in_issues()
    {
        await using var harness = await ClaimedAsync();
        await WearAsync(harness, "legacy_id", "42");
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var outcome = await accounts.ValidateAsync(Owner, Password, client: null, TestContext.Current.CancellationToken);

        Assert.Equal("42", outcome.Principal!.FindFirstValue("legacy_id"));
    }

    [Fact]
    public async Task A_claim_the_user_gives_for_itself_survives_a_session_being_loaded_again()
    {
        await using var harness = await ClaimedAsync();
        await WearAsync(harness, "legacy_id", "42");
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false, cancellationToken: TestContext.Current.CancellationToken);

        var loaded = await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken);
        var fromTheCache = await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal("42", loaded!.FindFirstValue("legacy_id"));
        Assert.Equal("42", fromTheCache!.FindFirstValue("legacy_id"));
    }

    [Fact]
    public async Task A_user_with_no_claims_of_its_own_carries_only_what_the_account_issues()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false, cancellationToken: TestContext.Current.CancellationToken);

        var principal = await Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.All(principal!.Claims, claim => Assert.True(AuthPrincipal.Issues(claim.Type), claim.Type));
    }

    [Theory]
    [InlineData(ClaimTypes.Role)]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sid")]
    [InlineData("rask:tenant")]
    public async Task A_claim_the_account_issues_itself_is_not_the_users_to_give(string type)
    {
        await using var harness = await ClaimedAsync();
        await WearAsync(harness, type, "admin");
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false, cancellationToken: TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sessions(harness).ResumeAsync(sessionId, TestContext.Current.CancellationToken));

        Assert.Contains(type, refusal.Message, StringComparison.Ordinal);
    }

    private static async Task WearAsync(AuthHarness harness, string type, string value)
    {
        await using var db = harness.NewContext();
        var owner = await db.Set<TestUser>().SingleAsync(u => u.Email == Owner, TestContext.Current.CancellationToken);
        owner.Wear(type, value);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static IAuthSessions Sessions(AuthHarness harness) =>
        harness.Services.GetRequiredService<IAuthSessions>();

    private static async Task<AuthHarness> ClaimedAsync(Action<AuthOptions>? configure = null, TimeProvider? clock = null)
    {
        var harness = new AuthHarness(configure, clock: clock);
        await harness.StartAsync();

        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        var owner = await accounts.RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null);
        Assert.True(owner.Result.Succeeded, $"harness setup failed: {owner.Result.Error}");

        return harness;
    }
}

/// <summary>A clock a test moves by hand.</summary>
public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
