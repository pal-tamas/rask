namespace Rask.Cqrs;

/// <summary>
///     Carries a message to a handler in another process. Implemented by a transport package
///     (<c>Rask.Cqrs.Client</c>), called by generated code; you inject <see cref="IDispatcher" />, not
///     this.
/// </summary>
/// <remarks>
///     <para>
///         It exists to keep the reflection-free promise at the one place it is hardest to keep.
///         <see cref="IDispatcher.QueryAsync{TResult}(IQuery{TResult}, System.Threading.CancellationToken)" />
///         must hand back a real <c>Task&lt;TResult&gt;</c>, and a transport that knew the result only as
///         a <see cref="System.Type" /> would have to build one through
///         <c>MakeGenericType</c> — runtime reflection, in the hot path, in exactly the package that
///         exists to avoid it. So the generator emits the call instead, closed over the concrete result
///         type, and the transport never needs to construct a generic.
///     </para>
///     <para>
///         The methods take the <see cref="RemoteContract" /> rather than looking it up, because the
///         generated caller already holds it.
///     </para>
/// </remarks>
public interface IRemoteDispatch
{
    /// <summary>Sends a query or a result-returning command and decodes the answer.</summary>
    /// <typeparam name="TResult">The result type, supplied by the generated caller.</typeparam>
    /// <param name="contract">The message's wire contract.</param>
    /// <param name="message">The message instance.</param>
    /// <param name="cancellationToken">Cancels the call, aborting the request in flight.</param>
    Task<TResult> SendAsync<TResult>(RemoteContract contract, object message, CancellationToken cancellationToken);

    /// <summary>Sends a command that returns no value.</summary>
    /// <param name="contract">The message's wire contract.</param>
    /// <param name="message">The message instance.</param>
    /// <param name="cancellationToken">Cancels the call, aborting the request in flight.</param>
    Task SendAsync(RemoteContract contract, object message, CancellationToken cancellationToken);

    /// <summary>Sends a notification for the other side's handlers to react to.</summary>
    /// <param name="contract">The notification's wire contract.</param>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Cancels the call, aborting the request in flight.</param>
    Task PublishAsync(RemoteContract contract, object notification, CancellationToken cancellationToken);
}
