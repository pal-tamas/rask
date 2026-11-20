using Microsoft.Extensions.DependencyInjection;
using Rask.Server.SignOut;
using Rask.WebPush;
using PushSubscription = Rask.Wire.PushSubscription;

namespace Rask.Server.Tests.SignOut;

/// <summary>Signing out on the Server host lets go of the browser's push subscription, when there is one to let go.</summary>
public sealed class PushSignOutReleaseTests
{
    [Fact]
    public async Task Signing_out_releases_the_subscription_the_browser_holds()
    {
        var push = new RecordingPush();
        using var services = new ServiceCollection().AddSingleton<IPush>(push).BuildServiceProvider();
        var step = new PushSignOutRelease(services, () => Task.FromResult<string?>("https://push.example/abc"));

        await step.BeforeSignOut();

        Assert.Equal(["https://push.example/abc"], push.Released);
    }

    [Fact]
    public async Task A_browser_that_never_subscribed_releases_nothing()
    {
        var push = new RecordingPush();
        using var services = new ServiceCollection().AddSingleton<IPush>(push).BuildServiceProvider();
        var step = new PushSignOutRelease(services, () => Task.FromResult<string?>(null));

        await step.BeforeSignOut();

        Assert.Empty(push.Released);
    }

    [Fact]
    public async Task An_app_without_web_push_signs_out_without_asking_the_browser()
    {
        var asked = false;
        using var services = new ServiceCollection().BuildServiceProvider();
        var step = new PushSignOutRelease(services, () =>
        {
            asked = true;
            return Task.FromResult<string?>("https://push.example/abc");
        });

        await step.BeforeSignOut();

        Assert.False(asked);
    }

    private sealed class RecordingPush : IPush
    {
        public List<string> Released { get; } = [];

        public string? PublicKey => null;

        public Task<bool> Release(string endpoint, CancellationToken cancellationToken = default)
        {
            Released.Add(endpoint);
            return Task.FromResult(true);
        }

        public Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WebPushResult> Send(PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> Deliver(WebPushMessage message, Guid? userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
