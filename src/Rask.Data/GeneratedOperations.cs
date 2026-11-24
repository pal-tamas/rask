using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The run of every generated operation entry — <c>await Supply.Change(id, version, details, parts)</c> — that a
///     public method of an aggregate gets: one transaction around the load, the method and the save.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, which lives in the application's assembly and so cannot reach an
///         <c>internal</c> member. Not an API to write against: call the entry on the aggregate.
///     </para>
///     <para>
///         In order, inside one <c>Db.Transaction</c>: the roles its <c>b.Operation(x =&gt; x.M, roles: …)</c> names
///         are asked of the signed-in principal, before anything is read; the aggregate is loaded at the version the
///         caller read (<c>Find(id, version)</c>); the method runs on it; and it is saved, which asks its
///         <c>Validate()</c> first. A refusal anywhere — a stale version, a rule, a <c>Refuse</c> — rolls back the
///         whole operation and reaches the caller as the <see cref="RaskValidationException" /> a form shows.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedOperations
{
    /// <summary>Runs one operation of <typeparamref name="TAggregate" /> as one write.</summary>
    /// <typeparam name="TAggregate">The aggregate the operation belongs to.</typeparam>
    /// <param name="operation">The method's name, for the refusal a missing role gets.</param>
    /// <param name="roles">
    ///     The roles its <c>Configure</c> names, comma-separated as <c>[Authorize(Roles = …)]</c> takes them — any one
    ///     admits — or <see langword="null" /> for none asked.
    /// </param>
    /// <param name="key">The id of the aggregate.</param>
    /// <param name="version">The version the caller read, or <see langword="null" /> for an aggregate with none.</param>
    /// <param name="apply">Calls the method on the loaded aggregate.</param>
    /// <param name="cancellationToken">Cancels the operation; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the operation has committed.</returns>
    /// <exception cref="ForbiddenException">The principal holds none of <paramref name="roles" />.</exception>
    /// <exception cref="KeyNotFoundException">No aggregate has <paramref name="key" />, or it is soft-deleted.</exception>
    /// <exception cref="RaskValidationException">
    ///     It was saved by someone else since <paramref name="version" />, or a rule refused the change.
    /// </exception>
    public static Task Run<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        string operation,
        string? roles,
        object key,
        int? version,
        Func<TAggregate, Task> apply,
        CancellationToken cancellationToken = default)
        where TAggregate : class, IAggregate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);
        cancellationToken = Ambient.Or(cancellationToken);

        Demand<TAggregate>(operation, roles);

        return Db.Transaction(async () =>
        {
            var aggregate = (version is { } read
                                ? await GeneratedModelWrites.Find<TAggregate>(key, read, cancellationToken).ConfigureAwait(false)
                                : await GeneratedModelWrites.Find<TAggregate>(key, cancellationToken).ConfigureAwait(false))
                            ?? throw GeneratedModelWrites.Missing<TAggregate>(key);

            await apply(aggregate).ConfigureAwait(false);
            await GeneratedModelWrites.Save(aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Builds a new <typeparamref name="TAggregate" /> with its factory and saves it as one write — what
    ///     <c>await Supply.Register(company, details, parts)</c> does.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the factory builds.</typeparam>
    /// <typeparam name="TId">Its key type.</typeparam>
    /// <param name="operation">The factory's name, for the refusal a missing role gets.</param>
    /// <param name="roles">The roles its <c>Configure</c> names, comma-separated, or <see langword="null" /> for none.</param>
    /// <param name="build">Calls the factory.</param>
    /// <param name="cancellationToken">Cancels the operation; unset, the work this call belongs to does.</param>
    /// <returns>The id of the saved aggregate, once committed.</returns>
    /// <exception cref="ForbiddenException">The principal holds none of <paramref name="roles" />.</exception>
    /// <exception cref="RaskValidationException">A rule refused what the factory built.</exception>
    public static async Task<TId> Build<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate, TId>(
        string operation,
        string? roles,
        Func<Task<TAggregate>> build,
        CancellationToken cancellationToken = default)
        where TAggregate : Entity<TId>, IAggregate
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(build);
        cancellationToken = Ambient.Or(cancellationToken);

        Demand<TAggregate>(operation, roles);

        return await Db.Transaction(async () =>
        {
            var aggregate = await build().ConfigureAwait(false);
            await GeneratedModelWrites.Save(aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
            return aggregate.Id;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Tries one operation without doing it — what a generated <c>Supply.DryRunChange(id, version, …)</c> does, and
    ///     a form over the operation asks as it is filled in: the aggregate loaded afresh at <paramref name="version" />,
    ///     the method run on that copy, and the rules a save would ask asked of it, with nothing written, locked or
    ///     announced.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the operation belongs to.</typeparam>
    /// <param name="key">The id of the aggregate.</param>
    /// <param name="version">The version the caller read, or <see langword="null" /> for an aggregate with none.</param>
    /// <param name="apply">Calls the method on the copy.</param>
    /// <param name="cancellationToken">Cancels the try; unset, the work this call belongs to does.</param>
    /// <returns>
    ///     Every failure the operation would be refused for — a stale version, a <c>Refuse</c>, a rule — each under the
    ///     field it names; empty when it would go through.
    /// </returns>
    /// <exception cref="KeyNotFoundException">No aggregate has <paramref name="key" />, or it is soft-deleted.</exception>
    public static Task<IReadOnlyList<FieldFailure>> Try<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        object key,
        int? version,
        Func<TAggregate, Task> apply,
        CancellationToken cancellationToken = default)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);
        cancellationToken = Ambient.Or(cancellationToken);

        return DryRun.Of(async () =>
        {
            var aggregate = (version is { } read
                                ? await GeneratedModelWrites.Find<TAggregate>(key, read, cancellationToken).ConfigureAwait(false)
                                : await GeneratedModelWrites.Find<TAggregate>(key, cancellationToken).ConfigureAwait(false))
                            ?? throw GeneratedModelWrites.Missing<TAggregate>(key);
            await apply(aggregate).ConfigureAwait(false);
            return aggregate;
        }, cancellationToken);
    }

    /// <summary>
    ///     Tries a factory without saving what it builds — what a generated <c>Supply.DryRunRegister(…)</c> does: the
    ///     aggregate built and the rules a save would ask asked of it, with nothing written, locked or announced.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the factory builds.</typeparam>
    /// <param name="build">Calls the factory.</param>
    /// <param name="cancellationToken">Cancels the try; unset, the work this call belongs to does.</param>
    /// <returns>Every failure the save would be refused for, each under the field it names; empty when none.</returns>
    public static Task<IReadOnlyList<FieldFailure>> TryBuild<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        Func<Task<TAggregate>> build,
        CancellationToken cancellationToken = default)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(build);
        return DryRun.Of(build, Ambient.Or(cancellationToken));
    }

    /// <summary>
    ///     Makes one argument of an operation from a form's field, keeping what its rules refuse instead of throwing it,
    ///     so a form hears every field it got wrong at once — what a generated <c>Form.For</c> reads its fields with.
    /// </summary>
    /// <typeparam name="T">The argument: a value object made through its rules, or a value as given.</typeparam>
    /// <param name="make">Makes it: <c>() =&gt; DriverName.Create(input.Name, "Name")</c>.</param>
    /// <param name="failures">Where a refusal's failures go.</param>
    /// <returns>What <paramref name="make" /> made, or the default when its rules refused it.</returns>
    public static T? Given<T>(Func<T> make, ICollection<FieldFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentNullException.ThrowIfNull(failures);
        try
        {
            return make();
        }
        catch (RaskValidationException refused)
        {
            foreach (var failure in refused.Failures)
            {
                failures.Add(failure);
            }
            return default;
        }
    }

    /// <summary>
    ///     Whether the principal of the work in flight holds one of <paramref name="roles" /> — what a generated
    ///     <c>Supply.MaySetOpening</c> answers.
    /// </summary>
    /// <param name="roles">The roles, comma-separated; <see langword="null" /> or empty admits anyone.</param>
    /// <returns>True when it may run the operation.</returns>
    public static bool Admits(string? roles) =>
        RolesOf(roles) is not { Length: > 0 } named || named.Any(static role => Current.IsInRole(role));

    private static void Demand<TAggregate>(string operation, string? roles)
    {
        if (Admits(roles))
        {
            return;
        }

        var signedIn = Current.Principal?.Identity?.IsAuthenticated == true;
        throw new ForbiddenException(
            $"{typeof(TAggregate).Name}.{operation} needs one of the roles '{roles}', which " +
            (signedIn ? "the signed-in user does not hold." : "nobody signed in holds."),
            signedIn);
    }

    private static string[]? RolesOf(string? roles) =>
        roles?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
