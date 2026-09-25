namespace Rask.Cqrs;

/// <summary>
///     Decides who may open one <typeparamref name="TSubscription" /> — the customer who owns the order it asks about,
///     an admin.
/// </summary>
/// <remarks>
///     <para>
///         Asked once, when the subscription opens, in the subscriber's own scope — so the signed-in user is the one
///         asking, and a handler's services are available the same way. Write one per subscription record and the
///         generator registers it, like a handler:
///     </para>
///     <code>
///     public sealed class WatchOrderPolicy : IWatchPolicy&lt;WatchOrder&gt;
///     {
///         public async Task&lt;bool&gt; CanWatchAsync(WatchOrder watch, CancellationToken ct) =&gt;
///             (await Order.Read.Where(o =&gt; o.Id == watch.OrderId).FirstOrDefaultAsync())?.CustomerId == Current.UserId;
///     }
///     </code>
///     <para>
///         One class may implement several of these where the rule is the same. <b>No policy means nobody may watch</b>:
///         the subscription settles on <see cref="UnauthorizedAccessException" /> before anything is delivered, in this
///         process and from a browser alike, so forgetting one can never leak.
///     </para>
/// </remarks>
/// <typeparam name="TSubscription">The subscription record this admits.</typeparam>
public interface IWatchPolicy<in TSubscription>
{
    /// <summary>Whether the current subscriber may open <paramref name="subscription" />.</summary>
    /// <param name="subscription">What is being asked for, with its own typed values.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<bool> CanWatchAsync(TSubscription subscription, CancellationToken cancellationToken);
}
