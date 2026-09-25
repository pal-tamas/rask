namespace Rask.Cqrs;

/// <summary>
///     Dispatches a received message to its local handler and hands back the result as
///     <see cref="object" />.
/// </summary>
/// <param name="provider">The request's service scope.</param>
/// <param name="message">The decoded message.</param>
/// <param name="cancellationToken">Cancels the handler; the endpoint passes the request's abort token.</param>
/// <returns>The handler's result, or null for a void command or a notification.</returns>
public delegate Task<object?> RemoteLocalInvoker(
    IServiceProvider provider,
    object message,
    CancellationToken cancellationToken);
