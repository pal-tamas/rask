using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;
using Rask.Generators.Shared;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Recognises a provider's "this key points nowhere" error on a row keyed by several columns that a save added —
///     a link to an activity that does not exist — and puts it under the list a form carries those rows in:
///     <c>ActivityIds</c> for a link, <c>Columns</c> for a row with fields of its own.
/// </summary>
/// <remarks>
///     By the provider's own code, as <see cref="ReferencedDelete" /> recognises the other side of the same error. SQL
///     Server raises that code for a CHECK constraint too, which says nothing about a missing row and is left as it is.
/// </remarks>
internal static class HeldRowReference
{
    /// <summary>What a form says when one of the rows it chose is not there.</summary>
    internal const string Message = "Something chosen here does not exist.";

    private const string SqlServerCheck = "CHECK constraint";

    /// <summary>
    ///     The validation failure <paramref name="failure" /> stands for, when it is a row keyed by several columns
    ///     that the database refused because a key it holds points nowhere; otherwise null.
    /// </summary>
    internal static RaskValidationException? Translate(Exception? failure)
    {
        if (failure is not DbUpdateException { InnerException: DbException provider } update ||
            !ReferencedDelete.IsForeignKeyViolation(provider) ||
            provider.Message.Contains(SqlServerCheck, StringComparison.Ordinal))
        {
            return null;
        }

        var fields = update.Entries
            .Where(static entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(static entry => entry.Metadata)
            .Where(ReplacedChildren.IsKeyedByParts)
            .Select(FieldOf)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return fields is [{ } field]
            ? new RaskValidationException([new FieldFailure(Message, [field], Source: provider.Data["ConstraintName"] as string)], failure)
            : null;
    }

    // The list on the holder's form: the ids of the one other part for a link, the rows for anything more.
    private static string? FieldOf(IEntityType type)
    {
        var held = type.GetForeignKeys()
            .Select(static key => key.PrincipalToDependent)
            .Where(static navigation => navigation is { IsCollection: true })
            .ToList();

        if (held is not [{ } navigation])
        {
            return null;
        }

        return IsLink(type, navigation.ForeignKey) ? FormListName.Ids(navigation.Name) : navigation.Name;
    }

    // The holder's part and one other, and nothing else: what the form carries as a list of ids.
    private static bool IsLink(IEntityType type, IForeignKey holder) =>
        type.FindPrimaryKey()!.Properties.Count(property => !holder.Properties.Contains(property)) == 1 &&
        type.GetProperties().All(static property => property.IsPrimaryKey() || property.IsShadowProperty()) &&
        !type.GetComplexProperties().Any() &&
        !type.GetNavigations().Any(static navigation => navigation.IsCollection);
}
