using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The store's rules for a generated form model: every unique rule its aggregate declares with a message —
///     <c>IsUnique("…")</c> — and, for a tenant's aggregate, every reference the save would check names a row of the
///     tenant in flight (<c>WhenMissing("…")</c>), asked before the save, while the form is filled in.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, which lives in the application's assembly and so cannot reach an
///         <c>internal</c> member. Not an API to write against: bind a form to the aggregate's model and the
///         form asks by itself.
///     </para>
///     <para>
///         It is a courtesy, never the rule. Two people can pass it at the same moment, and the save — the
///         index, or the declared rule asked again inside the save's transaction — still has the last word.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedStoreRules
{
    // What each rule last answered for a model, so a rule over two fields is asked once when both are
    // committed with the values it has already seen. Held by the model: it goes when the form does.
    internal static readonly ConditionalWeakTable<object, Dictionary<string, UniqueAnswer>> Answers = [];

    /// <summary>Whether there is a database to ask from <paramref name="services" />.</summary>
    /// <param name="services">The scope the check would run in — a live session's — or <see langword="null" />.</param>
    /// <returns><see langword="false" /> where no context is registered, so the form asks nothing.</returns>
    public static bool CanAsk(IServiceProvider? services) =>
        Db.HasContext || services?.GetService<AmbientContextBinding>() is not null;

    /// <summary>
    ///     What the unique rules of <typeparamref name="TEntity" /> say about <paramref name="model" />: one
    ///     failure for each rule another row already satisfies.
    /// </summary>
    /// <typeparam name="TEntity">The aggregate.</typeparam>
    /// <typeparam name="TModel">Its generated form model.</typeparam>
    /// <param name="services">The scope the check runs in — a live session's — or <see langword="null" />.</param>
    /// <param name="model">The form's model.</param>
    /// <param name="field">The committed field, as the model names it, or <see langword="null" /> for every rule.</param>
    /// <param name="valueOf">The model's value for a property of the aggregate, by the aggregate's name for it.</param>
    /// <param name="keyOf">The key of the row the model was filled from, or <see langword="null" /> for a new one.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The failures, each over the fields its rule names; empty when nothing is taken.</returns>
    public static ValueTask<IReadOnlyList<FieldFailure>> Check<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TModel>(
        IServiceProvider? services,
        TModel model,
        string? field,
        Func<TModel, string, object?> valueOf,
        Func<TModel, object?> keyOf,
        CancellationToken cancellationToken)
        where TEntity : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(valueOf);
        ArgumentNullException.ThrowIfNull(keyOf);

        return CheckAsync<TEntity, TModel>(services, model, field, valueOf, keyOf, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<FieldFailure>> CheckAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TModel>(
        IServiceProvider? services,
        TModel model,
        string? field,
        Func<TModel, string, object?> valueOf,
        Func<TModel, object?> keyOf,
        CancellationToken cancellationToken)
        where TEntity : class
        where TModel : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The host has this open around a session's work already. Opened here only for a caller that has not,
        // so the tenant is the one resolved for THIS scope and never one left over from somewhere else.
        using var scope = Db.ScopeServices is null && services is not null ? Db.OpenScope(services) : null;

        // A context of its own from the factory: the scope is a session's, and a session-long DbContext is
        // shared by everything the session does.
        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);

        var unique = await new UniqueStoreRules<TEntity, TModel>(valueOf, keyOf).Check(context, model, field, cancellationToken)
            .ConfigureAwait(false);
        var references = await new ReferenceStoreRules<TEntity, TModel>(valueOf).Check(context, model, field, cancellationToken)
            .ConfigureAwait(false);

        return references.Count == 0 ? unique : [.. unique, .. references];
    }
}
