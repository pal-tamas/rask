using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     <c>Sessions.Forget(user)</c>: a change the app knows of and no save says — a user choosing another partner — drops
///     what <c>.Keep(…)</c> kept about that user, for every page of theirs, and for no one else.
/// </summary>
public sealed class SessionsForgetTests
{
    [Fact]
    public async Task Forgetting_a_user_makes_their_next_check_ask_the_app_again()
    {
        await using var services = Start();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<PartnerCheck>();
        var alice = User("alice", partner: "1");
        var before = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);
        check.Chose("alice", "2");

        services.GetRequiredService<ISessions>().Forget("alice");
        var after = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        Assert.Same(alice, before);
        Assert.Null(after);
        Assert.Equal(2, check.Loads);
    }

    [Fact]
    public async Task Forgetting_one_user_leaves_the_others_kept()
    {
        await using var services = Start();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<PartnerCheck>();
        var alice = User("alice", partner: "1");
        var bob = User("bob", partner: "1");
        await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);
        await revalidator.Revalidate(bob, TestContext.Current.CancellationToken);

        services.GetRequiredService<ISessions>().Forget(alice);
        await revalidator.Revalidate(bob, TestContext.Current.CancellationToken);

        Assert.Equal(2, check.Loads);
        Assert.Equal(["alice", "bob"], check.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Two_tabs_of_the_user_both_see_the_new_answer()
    {
        await using var services = Start();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<PartnerCheck>();
        var firstTab = User("alice", partner: "1");
        var secondTab = User("alice", partner: "1", tab: "second");
        await revalidator.Revalidate(firstTab, TestContext.Current.CancellationToken);
        await revalidator.Revalidate(secondTab, TestContext.Current.CancellationToken);
        check.Chose("alice", "2");

        using (Ambient.Enter(services))
        {
            Sessions.Forget(User("alice", partner: "2"));
        }

        var first = await revalidator.Revalidate(firstTab, TestContext.Current.CancellationToken);
        var second = await revalidator.Revalidate(secondTab, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(4, check.Loads);
    }

    [Fact]
    public async Task A_forget_during_an_answer_on_its_way_wins()
    {
        await using var services = Start();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<PartnerCheck>();
        var alice = User("alice", partner: "1");
        var reading = check.HoldNextRead();
        var stale = revalidator.Revalidate(alice, TestContext.Current.CancellationToken).AsTask();
        await reading.Started;

        check.Chose("alice", "2");
        services.GetRequiredService<ISessions>().Forget("alice");
        reading.Finish();
        var onItsWay = await stale;
        var next = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        Assert.Same(alice, onItsWay);
        Assert.Null(next);
        Assert.Equal(2, check.Loads);
    }

    private static ClaimsPrincipal User(string id, string partner, string tab = "first") =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id), new Claim("partner", partner), new Claim("tab", tab)], "TestCookie"));

    private static ServiceProvider Start()
    {
        var services = new ServiceCollection();
        services.AddSessionRevalidator<PartnerCheck>().Keep(1.Hour);
        return services.BuildServiceProvider();
    }

    // The app's own revalidator: a user stands while the partner their principal names is the one they chose last.
    internal sealed class PartnerCheck : ISessionRevalidator
    {
        private readonly ConcurrentDictionary<string, string> _chosen = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _asked = new();
        private Hold? _hold;

        public int Loads => _asked.Count;

        public IEnumerable<string> Asked => _asked;

        public void Chose(string user, string partner) => _chosen[user] = partner;

        public Hold HoldNextRead() => _hold = new Hold();

        public async ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            var user = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            _asked.Enqueue(user);
            var chosen = _chosen.GetValueOrDefault(user, "1");
            if (Interlocked.Exchange(ref _hold, null) is { } hold)
            {
                await hold.Wait();
            }

            return principal.FindFirstValue("partner") == chosen ? principal : null;
        }
    }

    // A read that has taken its data and is held before it answers, as a slow database read would be.
    internal sealed class Hold
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Finish() => _finish.TrySetResult();

        public Task Wait()
        {
            _started.TrySetResult();
            return _finish.Task;
        }
    }
}
