using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

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

        var sessionId = await Sessions(harness).StartAsync(owner.Id, "10.0.0.1", "test-agent", persistent: true);
        var principal = await Sessions(harness).ResumeAsync(sessionId);

        Assert.NotNull(principal);
        Assert.Equal(owner.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(Owner, principal.Identity!.Name);
        Assert.True(principal.IsInRole(RaskRoles.Admin));
        Assert.Equal(sessionId.ToString(), principal.FindFirstValue("sid"));

        await using var db = harness.NewContext();
        var row = await db.Set<Session>().SingleAsync(s => s.Id == sessionId);
        Assert.Equal("10.0.0.1", row.IpAddress);
        Assert.Equal("test-agent", row.UserAgent);
        Assert.True(row.Persistent);
    }

    [Fact]
    public async Task An_ended_session_does_not_resume()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, persistent: false);

        await Sessions(harness).EndAsync(sessionId);

        Assert.Null(await Sessions(harness).ResumeAsync(sessionId));

        // Soft-deleted, like every aggregate: the row is still there for a device list to say "signed out".
        await using var db = harness.NewContext();
        Assert.NotNull(await db.Set<Session>().IgnoreQueryFilters().SingleAsync(s => s.Id == sessionId));
    }

    [Fact]
    public async Task Ending_every_other_session_keeps_the_one_asking()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        var sessions = Sessions(harness);

        var here = await sessions.StartAsync(owner.Id, null, "laptop", false);
        var phone = await sessions.StartAsync(owner.Id, null, "phone", false);
        var tablet = await sessions.StartAsync(owner.Id, null, "tablet", false);

        Assert.Equal(2, await sessions.EndAllAsync(owner.Id, except: here));

        Assert.NotNull(await sessions.ResumeAsync(here));
        Assert.Null(await sessions.ResumeAsync(phone));
        Assert.Null(await sessions.ResumeAsync(tablet));
    }

    [Fact]
    public async Task A_session_left_unused_past_its_lifetime_does_not_resume()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        await using var harness = await ClaimedAsync(o => o.ExpireTimeSpan = TimeSpan.FromHours(1), clock);
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false);

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await Sessions(harness).ResumeAsync(sessionId));
    }

    [Fact]
    public async Task A_session_in_use_slides_its_expiry_forward()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        await using var harness = await ClaimedAsync(o => o.ExpireTimeSpan = TimeSpan.FromHours(1), clock);
        var owner = (await harness.UserAsync(Owner))!;
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false);

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
        var sessionId = await Sessions(harness).StartAsync(owner.Id, null, null, false);

        await using (var db = harness.NewContext())
        {
            db.Remove(await db.Set<TestUser>().SingleAsync(u => u.Id == owner.Id));
            await db.SaveChangesAsync();
        }

        Assert.Null(await ResumeUncachedAsync(harness, sessionId));
    }

    // The resume cache would otherwise answer from the first read for 30 seconds of wall-clock time.
    private static Task<ClaimsPrincipal?> ResumeUncachedAsync(AuthHarness harness, Guid sessionId)
    {
        harness.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Remove("Rask.Auth.Session:" + sessionId.ToString("N"));
        return Sessions(harness).ResumeAsync(sessionId);
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
