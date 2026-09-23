namespace Rask.Cqrs;

/// <summary>
///     Says which <typeparamref name="TScope" /> a notification is about, so it reaches only the subscribers watching
///     that one — the customer on one order's page, not every customer.
/// </summary>
/// <remarks>
///     <para>Put it on the property that holds the thing's key:</para>
///     <code>
///     public sealed record OrderShipped([For&lt;Order&gt;] Guid OrderId, string Status) : INotification;
///
///     var shipped = QueryClient.Subscribe&lt;OrderShipped&gt;(Id);   // only this order's
///     </code>
///     <para>
///         A subscription to a scoped notification names its key, and is admitted by the
///         <see cref="IWatchPolicy{TScope}" /> for <typeparamref name="TScope" />. With no policy nobody may watch, so
///         forgetting one fails closed. <typeparamref name="TScope" /> is what the key identifies — an aggregate, a
///         user — and one policy covers every notification about it.
///     </para>
///     <para>
///         The key compares with <see cref="object.Equals(object)" />, and crosses the wire as its invariant string: a
///         <see cref="Guid" />, a number, a <see cref="string" /> or any <see cref="IParsable{TSelf}" /> id.
///     </para>
/// </remarks>
/// <typeparam name="TScope">What the key identifies.</typeparam>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ForAttribute<TScope> : Attribute;
