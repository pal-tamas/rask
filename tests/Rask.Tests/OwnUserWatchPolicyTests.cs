using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Tests;

/// <summary>
///     Every scope's watch policy until the app writes one: a signed-in user may watch what is keyed by their own id,
///     and nothing else.
/// </summary>
public sealed class OwnUserWatchPolicyTests
{
    private sealed class Account;

    private sealed class Order;

    [Fact]
    public async Task A_user_may_watch_their_own_id_and_no_one_else_s()
    {
        var me = Guid.NewGuid();
        var policy = new OwnUserWatchPolicy<Account>(new Signed(me));

        Assert.True(await policy.CanWatchAsync(me, CancellationToken.None));
        Assert.False(await policy.CanWatchAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_signed_out_visitor_may_watch_nothing()
    {
        var policy = new OwnUserWatchPolicy<Account>(new Signed(null));

        Assert.False(await policy.CanWatchAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_key_that_is_not_a_guid_is_never_the_user_s()
    {
        var policy = new OwnUserWatchPolicy<Order>(new Signed(Guid.NewGuid()));

        Assert.False(await policy.CanWatchAsync(42, CancellationToken.None));
    }

    [Fact]
    public void An_app_that_says_nothing_gets_it_for_every_scope()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0")).Build<TestApp>();

        using var scope = app.Services.CreateScope();

        Assert.IsType<OwnUserWatchPolicy<Account>>(scope.ServiceProvider.GetService<IWatchPolicy<Account>>());
        Assert.IsType<OwnUserWatchPolicy<Order>>(scope.ServiceProvider.GetService<IWatchPolicy<Order>>());
    }

    private sealed class Signed(Guid? user) : IPrincipalSource
    {
        public ClaimsPrincipal? Current { get; } = user is { } id
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test"))
            : null;
    }
}
