namespace Rask.Cqrs;

/// <summary>
///     Decides who may watch the notifications about one <typeparamref name="TScope" /> — the customer who owns an
///     order, an admin.
/// </summary>
/// <remarks>
///     <para>
///         Every subscription to a notification marked <see cref="ForAttribute{TScope}" /> asks this first, in the
///         subscriber's own scope, so the signed-in user is the one asking. No policy means nobody may watch: a
///         forgotten policy fails closed rather than showing one customer another's order. Write one per scope and the
///         generator registers it, like a handler:
///     </para>
///     <code>
///     public sealed class WatchingOrders : IWatchPolicy&lt;Order&gt;
///     {
///         public async Task&lt;bool&gt; CanWatchAsync(object key, CancellationToken ct) =&gt;
///             (await Order.Find((Guid)key)).CustomerId == Current.UserId;
///     }
///     </code>
///     <para>
///         A subscription that is refused ends with an <see cref="UnauthorizedAccessException" /> before anything is
///         delivered.
///     </para>
/// </remarks>
/// <typeparam name="TScope">What the watched key identifies.</typeparam>
public interface IWatchPolicy<TScope>
{
    /// <summary>Whether the current subscriber may watch the notifications for <paramref name="key" />.</summary>
    /// <param name="key">The key the subscriber asked for, of the type the <see cref="ForAttribute{TScope}" /> property has.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<bool> CanWatchAsync(object key, CancellationToken cancellationToken);
}
