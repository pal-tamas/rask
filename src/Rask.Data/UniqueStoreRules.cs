using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Asks <typeparamref name="TEntity" />'s declared unique rules about a form model, through a context of
///     the scope the form lives in — so the rows that count are the current tenant's and nobody else's.
/// </summary>
internal sealed class UniqueStoreRules<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TModel>(
    IServiceProvider? services,
    Func<TModel, string, object?> valueOf,
    Func<TModel, object?> keyOf)
    where TEntity : class
    where TModel : class
{
    /// <summary>
    ///     One failure for each rule another row already satisfies: the rules that name
    ///     <paramref name="field" />, or every rule when it is <see langword="null" />.
    /// </summary>
    internal async ValueTask<IReadOnlyList<FieldFailure>> Check(
        TModel typed, string? field, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The host has this open around a session's work already. Opened here only for a caller that has not,
        // so the tenant is the one resolved for THIS scope and never one left over from somewhere else.
        using var scope = Db.ScopeServices is null && services is not null ? Db.OpenScope(services) : null;

        // A context of its own from the factory: the scope is a session's, and a session-long DbContext is
        // shared by everything the session does.
        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);

        if (context.Model.FindEntityType(typeof(TEntity)) is not { } entityType)
        {
            return [];
        }

        var key = keyOf(typed);
        List<FieldFailure>? failures = null;

        foreach (var rule in DeclaredUniqueRules.Rules(entityType))
        {
            if (About(rule, field) &&
                await TakenAsync(context, entityType, rule, typed, key, fresh: field is null, cancellationToken)
                    .ConfigureAwait(false))
            {
                (failures ??= []).Add(UniqueViolation.FailureOf(rule));
            }
        }

        return failures ?? [];
    }

    // The submit asks about every rule, a committed field about the rules that name it.
    private static bool About(IIndex rule, string? field) =>
        field is null || rule.Properties.Any(p => string.Equals(p.Name, field, StringComparison.Ordinal));

    private async Task<bool> TakenAsync(
        DbContext context,
        IEntityType entityType,
        IIndex rule,
        TModel model,
        object? key,
        bool fresh,
        CancellationToken cancellationToken)
    {
        var values = new object?[rule.Properties.Count + 1];
        for (var i = 0; i < rule.Properties.Count; i++)
        {
            // A NULL does not collide, and a field not filled in yet is one: nothing to ask.
            if ((values[i] = ValueOf(rule.Properties[i], model)) is null)
            {
                return false;
            }
        }

        values[^1] = key;

        var answers = GeneratedStoreRules.Answers.GetOrCreateValue(model);
        var name = rule.GetDatabaseName() ?? rule.Name ?? string.Join('+', rule.Properties.Select(static p => p.Name));

        // The submit always asks: it is the last look before the save, and somebody may have saved since.
        if (!fresh && Remembered(answers, name) is { } answer && answer.Values.AsSpan().SequenceEqual(values))
        {
            return answer.Taken;
        }

        var taken = await DeclaredUniqueRules.TakenAsync(
            context,
            entityType,
            rule,
            property => values[IndexOf(rule, property)],
            key is null ? null : [key],
            cancellationToken).ConfigureAwait(false);

        lock (answers)
        {
            answers[name] = new UniqueAnswer(values, taken);
        }

        return taken;
    }

    private static UniqueAnswer? Remembered(Dictionary<string, UniqueAnswer> answers, string name)
    {
        lock (answers)
        {
            return answers.GetValueOrDefault(name);
        }
    }

    private static int IndexOf(IIndex rule, string property)
    {
        for (var i = 0; i < rule.Properties.Count; i++)
        {
            if (string.Equals(rule.Properties[i].Name, property, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return rule.Properties.Count;
    }

    // In the property's own type. The tenant is never the model's to say: it is the one in flight, and with
    // none — or across all of them — the rule is left to the save rather than asked about everybody's rows.
    private object? ValueOf(IProperty property, TModel model)
    {
        if (string.Equals(property.Name, Columns.TenantId, StringComparison.Ordinal) &&
            ConventionRegistry.ScopeFor(typeof(TEntity)) == Tenancy.PerTenant)
        {
            return Current.Tenant is { } tenant && tenant != Tenant.Nobody
                ? TenantColumn.ValueFor(tenant, property.ClrType, typeof(TEntity).Name)
                : null;
        }

        // A text field nobody has typed into holds "", the entity's own default: not filled in, like a null.
        return valueOf(model, property.Name) is { } value and not ""
            ? InPropertyType(property, value)
            : null;
    }

    // A one-value value object is its value on the form and itself in the model: the column's own converter
    // turns the one into the other.
    private static object? InPropertyType(IProperty property, object value) =>
        !property.ClrType.IsInstanceOfType(value) &&
        property.GetValueConverter() is { } converter &&
        converter.ProviderClrType.IsInstanceOfType(value)
            ? converter.ConvertFromProvider(value)
            : value;
}
