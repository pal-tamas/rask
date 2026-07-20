namespace Rask.Core.Messaging;

/// <summary>
///     Transient, consumed-once user messages — a flash-message pattern. Registered
///     <b>scoped</b> per session (a Server WebSocket session or a WASM app instance); because a
///     client-side navigation does not recreate the session, a message queued before
///     <c>Navigator.NavigateTo(...)</c> survives the navigation and is shown once on the destination.
///     <para>
///         Producers inject <see cref="IToaster" /> and call <see cref="Success" /> / <see cref="Error" />
///         / … (or <see cref="Add" />). A single <c>ToastOutlet</c> mounted in the app layout subscribes
///         to <see cref="Changed" /> and <see cref="Consume" />s the queue, rendering each message once.
///     </para>
/// </summary>
public interface IToaster
{
    /// <summary>Raised after any message is added, so a mounted outlet can drain and repaint.</summary>
    event Action? Changed;

    /// <summary>Queue a message at the given <paramref name="level" />. Thread-safe.</summary>
    void Add(ToastLevel level, string message, string? title = null);

    /// <summary>Queue an <see cref="ToastLevel.Info" /> message.</summary>
    void Info(string message, string? title = null) => Add(ToastLevel.Info, message, title);

    /// <summary>Queue a <see cref="ToastLevel.Success" /> message.</summary>
    void Success(string message, string? title = null) => Add(ToastLevel.Success, message, title);

    /// <summary>Queue a <see cref="ToastLevel.Warning" /> message.</summary>
    void Warning(string message, string? title = null) => Add(ToastLevel.Warning, message, title);

    /// <summary>Queue an <see cref="ToastLevel.Error" /> message.</summary>
    void Error(string message, string? title = null) => Add(ToastLevel.Error, message, title);

    /// <summary>
    ///     Atomically remove and return every queued message. The queue is empty afterwards, so a
    ///     message is delivered to exactly one <see cref="Consume" /> call (consumed-once). Returns an
    ///     empty list when nothing is queued.
    /// </summary>
    IReadOnlyList<ToastMessage> Consume();
}
