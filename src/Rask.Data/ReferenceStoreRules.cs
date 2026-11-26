using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Asks of a form model of <typeparamref name="TEntity" /> what the save's reference check would (<see cref="ReferencedTenantCheck" />):
///     whether an id its fields hold names a row the tenant in flight reads — so "No such company." is told as the
///     field is left, not with the save.
/// </summary>
/// <remarks>
///     One query per reference a committed field holds, and none for a field left empty; an answer is kept for the
///     model until the value changes, and the submit asks again.
/// </remarks>
internal sealed class ReferenceStoreRules<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TModel>(
    Func<TModel, string, object?> valueOf)
    where TEntity : class
    where TModel : class
{
    /// <summary>A failure for each reference that misses: the references <paramref name="field" /> holds, or every one when null.</summary>
    internal async ValueTask<IReadOnlyList<FieldFailure>> Check(
        DbContext context, TModel model, string? field, CancellationToken cancellationToken)
    {
        if (context.Model.FindEntityType(typeof(TEntity)) is not { } entityType)
        {
            return [];
        }

        List<FieldFailure>? failures = null;
        var answers = GeneratedStoreRules.Answers.GetOrCreateValue(model);

        foreach (var key in ReferencedTenantCheck.KeysOf(entityType))
        {
            if (key.Properties is not [var property] ||
                (field is not null && !string.Equals(property.Name, field, StringComparison.Ordinal)) ||
                valueOf(model, property.Name) is not { } held)
            {
                continue;
            }

            var value = ModelValue.InPropertyType(property, held);
            var name = "ref:" + (key.GetConstraintName() ?? property.Name);

            // The submit always asks: somebody may have deleted the row since.
            if (field is null || Remembered(answers, name) is not { } answer || !Equals(answer.Values[0], value))
            {
                answer = new UniqueAnswer([value], await ReferencedTenantCheck.MissesAsync(context, key, [value], cancellationToken)
                    .ConfigureAwait(false));
                lock (answers)
                {
                    answers[name] = answer;
                }
            }

            if (answer.Taken)
            {
                (failures ??= []).Add(new FieldFailure(ReferencedTenantCheck.SentenceOf(key), [property.Name], Source: key.GetConstraintName()));
            }
        }

        return failures ?? [];
    }

    private static UniqueAnswer? Remembered(Dictionary<string, UniqueAnswer> answers, string name)
    {
        lock (answers)
        {
            return answers.GetValueOrDefault(name);
        }
    }
}
