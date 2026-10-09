using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The store's rules for a generated form model: every unique rule its aggregate declares with a message —
///     <c>IsUnique("…")</c> — asked before the save, while the form is filled in.
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

        return new UniqueStoreRules<TEntity, TModel>(services, valueOf, keyOf).Check(model, field, cancellationToken);
    }
}
