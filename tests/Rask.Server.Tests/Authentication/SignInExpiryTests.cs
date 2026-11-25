using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     A live page honours its principal's own <c>exp</c> claim: once it has passed (by the host's clock, past
///     <see cref="SignInExpiry.ClockSkew" />), the next event signs the reader out and runs nothing — with no
///     <see cref="Rask.Core.Authentication.ISessionRevalidator" /> registered at all.
/// </summary>
public class SignInExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_page_whose_principal_passed_its_exp_is_signed_out_at_its_next_event_without_a_revalidator()
    {
        using var host = M2SignedInPage.CreateHost(_ => { });
        var counter = host.Server.Services.GetRequiredService<M2Counter>();
        var (ws, sessionId, handlerId) = await M2SignedInPage.AttachAsync(host, ExpiresIn(TimeSpan.FromMinutes(10)));
        using var socket = ws;
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Clock(host).Advance(TimeSpan.FromMinutes(10) + SignInExpiry.ClockSkew + TimeSpan.FromSeconds(1));
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var afterExpiry = await ws.ReceiveTextAsync();

        Assert.Equal(1, counter.Count);
        Assert.Contains("/login", afterExpiry);
        Assert.NotEqual(true, host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>()
            .Current.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task A_principal_within_the_clock_skew_past_its_exp_still_acts()
    {
        using var host = M2SignedInPage.CreateHost(_ => { });
        var counter = host.Server.Services.GetRequiredService<M2Counter>();
        var (ws, _, handlerId) = await M2SignedInPage.AttachAsync(host, ExpiresIn(TimeSpan.FromMinutes(10)));
        using var socket = ws;

        Clock(host).Advance(TimeSpan.FromMinutes(10) + SignInExpiry.ClockSkew - TimeSpan.FromSeconds(1));
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task A_principal_without_exp_is_never_signed_out_by_the_clock()
    {
        using var host = M2SignedInPage.CreateHost(_ => { });
        var counter = host.Server.Services.GetRequiredService<M2Counter>();
        var (ws, _, handlerId) = await M2SignedInPage.AttachAsync(host);
        using var socket = ws;

        Clock(host).Advance(TimeSpan.FromDays(365));
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void The_earliest_exp_of_a_principal_s_identities_is_the_one_that_counts()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([Exp(Now.AddHours(2))], "a"));
        principal.AddIdentity(new ClaimsIdentity([Exp(Now.AddHours(1))], "b"));

        var lapsesAt = SignInExpiry.LapsesAt(principal);

        Assert.Equal(Now.AddHours(1) + SignInExpiry.ClockSkew, lapsesAt);
    }

    [Theory]
    [InlineData("1767225600.75", 1767225600L)]
    [InlineData("1767225600", 1767225600L)]
    public void A_numeric_date_with_or_without_a_fraction_is_read_to_its_whole_second(string exp, long seconds)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SignInExpiry.Claim, exp)], "a"));

        var lapsesAt = SignInExpiry.LapsesAt(principal);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(seconds) + SignInExpiry.ClockSkew, lapsesAt);
    }

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("")]
    [InlineData("1e300")]
    public void An_exp_that_is_no_date_is_not_taken_as_one(string exp)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SignInExpiry.Claim, exp)], "a"));

        var lapsesAt = SignInExpiry.LapsesAt(principal);

        Assert.Null(lapsesAt);
    }

    private static Claim[] ExpiresIn(TimeSpan span) => [Exp(Now + span)];

    private static Claim Exp(DateTimeOffset at) =>
        new(SignInExpiry.Claim, at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static StoppedClock Clock(RaskTestHost host) =>
        (StoppedClock)host.Server.Services.GetRequiredService<TimeProvider>();
}
