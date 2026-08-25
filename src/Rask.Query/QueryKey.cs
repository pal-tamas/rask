namespace Rask.Query;

/// <summary>
///     What identifies one cache entry.
/// </summary>
/// <remarks>
///     For a CQRS message the message <em>is</em> the key. Rask messages are records, so structural
///     equality comes free: <c>new GetOrders(Page: 1)</c> written in two components is one entry and
///     one round trip, with no key string to invent and nothing to keep in sync when a property is
///     added. That is the whole reason this wraps the dispatcher rather than an arbitrary callback.
///     <para>
///         The function form has no such luck and takes a caller-supplied <see cref="Name" />, which
///         is the one place in this package where two different things can collide under one key.
///     </para>
/// </remarks>
/// <param name="Group">
///     The type invalidation targets: the message type, or the result type for a named function.
/// </param>
/// <param name="Message">The message itself, compared structurally. Null for the function form.</param>
/// <param name="Name">The caller-supplied key for the function form. Null for a message.</param>
internal readonly record struct QueryKey(Type Group, object? Message, string? Name);
