namespace Rask.Data;

/// <summary>
///     A table that is plumbing beside the domain — a login session, a queue, a cache — whose rows a transaction writes
///     beside its aggregate without making it a second one: never counted against
///     <see cref="RaskDataOptions.OneAggregatePerTransaction" />.
/// </summary>
/// <remarks>
///     Rask's own tables (outbox, jobs, mail, sessions, passkeys, stored files, cache, push subscribers) say it already.
///     A fact about the type, read off the class, so saying it changes no migration.
/// </remarks>
#pragma warning disable CA1040 // a marker: the interface IS the declaration, read by the generator
public interface IInfrastructure;
#pragma warning restore CA1040
