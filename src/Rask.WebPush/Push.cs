using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>
/// Web Push with nothing injected: <c>await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way", "/orders/1042"))</c>
/// reaches every subscribed browser; <c>.To(userId)</c> reaches one person's. Resolved from the work in progress —
/// a handler, a render, a request, a job — like <c>Mail.Send</c>. Inject <see cref="IPush" /> where you would rather.
/// </summary>
public static class Push
{
    /// <summary>Sends to every subscriber — or, with <see cref="Pushing.To" />, to one user's browsers. Awaiting it returns how many were delivered.</summary>
    public static Pushing Send(WebPushMessage message, CancellationToken cancellationToken = default) =>
        new(null, message ?? throw new ArgumentNullException(nameof(message)), null, cancellationToken);

    /// <summary>Sends to one subscription and reports how the push service answered.</summary>
    public static Task<WebPushResult> Send(
        PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default) =>
        Resolve().Send(subscription, message, Ambient.Or(cancellationToken));

    /// <summary>Keeps a browser's subscription for the signed-in user. On the server host this is the line after <c>IWebPush.SubscribeAsync</c>.</summary>
    public static Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default) =>
        Resolve().Subscribe(subscription, Ambient.Or(cancellationToken));

    /// <summary>Forgets a browser's subscription.</summary>
    public static Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default) =>
        Resolve().Unsubscribe(endpoint, Ambient.Or(cancellationToken));

    /// <summary>The VAPID public key a browser subscribes with, or <see langword="null" /> until a key pair is configured.</summary>
    public static string? PublicKey => Resolve().PublicKey;

    /// <summary>Whether the battery is registered. Hidden: an app should not branch on it — a call with nothing registered throws and names the fix.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<IPush>() is not null;

    internal static readonly AsyncLocal<IPush?> Faked = new();

    internal static IPush Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Push was called outside any work in progress — a handler, a render, a request or a job — so there "
                + "is no app to reach. Inject IPush in the constructor there instead.");

        return services.GetService<IPush>()
            ?? throw new InvalidOperationException(
                "Push needs the Web Push battery registered: call builder.Services.AddRaskWebPush<AppDbContext>().");
    }
}

/// <summary>The same verbs on an injected <see cref="IPush" />.</summary>
public static class PushExtensions
{
    extension(IPush push)
    {
        /// <summary>Sends to every subscriber — or, with <see cref="Pushing.To" />, to one user's browsers.</summary>
        public Pushing Send(WebPushMessage message, CancellationToken cancellationToken = default) =>
            new(push ?? throw new ArgumentNullException(nameof(push)), message ?? throw new ArgumentNullException(nameof(message)), null, cancellationToken);
    }
}

/// <summary>A send that has not happened yet: <c>await Push.Send(message)</c> for everyone, <c>await Push.Send(message).To(userId)</c> for one person's browsers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Pushing
{
    private readonly IPush? _push;
    private readonly WebPushMessage _message;
    private readonly Guid? _userId;
    private readonly CancellationToken _cancellationToken;

    internal Pushing(IPush? push, WebPushMessage message, Guid? userId, CancellationToken cancellationToken)
    {
        _push = push;
        _message = message;
        _userId = userId;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Only this user's browsers.</summary>
    public Pushing To(Guid userId) => new(_push, _message, userId, _cancellationToken);

    /// <summary>The delivery, as a task: how many browsers received it.</summary>
    public Task<int> AsTask() => (_push ?? Push.Resolve()).Deliver(_message, _userId, Ambient.Or(_cancellationToken));

    /// <summary>Awaits the delivery.</summary>
    public TaskAwaiter<int> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Awaits the delivery, with or without the captured context.</summary>
    public ConfiguredTaskAwaitable<int> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);
}
