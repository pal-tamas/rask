using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.WebPush;

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
