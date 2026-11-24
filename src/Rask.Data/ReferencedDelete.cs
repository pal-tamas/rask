using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Recognises a provider's "this row is still referenced" error on a delete and finds the sentence the
///     model declared for it.
/// </summary>
/// <remarks>
///     <para>
///         By the provider's own code, as <see cref="UniqueViolation" /> does and for the same reason: SQLSTATE
///         <c>23503</c> on PostgreSQL — or <c>23001</c>, which it raises instead for a key declared
///         <c>ON DELETE RESTRICT</c>, the DDL EF Core's <c>DeleteBehavior.Restrict</c> becomes there —, error
///         <c>547</c> on SQL Server, and SQLite's fixed
///         <c>FOREIGN KEY constraint failed</c>. Those codes also cover the other side — a row inserted with a
///         key that points nowhere — and, on SQL Server, a CHECK constraint, so nothing is translated unless
///         the command that failed was deleting a row.
///     </para>
///     <para>
///         PostgreSQL and SQL Server name the constraint; SQLite names nothing, so the database is asked which
///         relationship still holds a row onto the deleted ones (<see cref="ReferenceProbe" />) and the answer is the
///         one the other two give. Where the answer would still be a guess between two sentences, the provider's
///         exception stays as it is.
///     </para>
///     <para>
///         A save's delete and an <c>ExecuteDelete</c> are answered alike: the first by the entries it deleted, the
///         second by the statement that failed.
///     </para>
/// </remarks>
internal static class ReferencedDelete
{
    /// <summary>The foreign key annotation <c>OnDelete(behavior, "…")</c> writes.</summary>
    internal const string RelationshipAnnotation = "Rask:ReferencedDeleteMessage";

    /// <summary>The entity type annotation <c>WhenReferenced("…")</c> writes.</summary>
    internal const string EntityAnnotation = "Rask:WhenReferencedMessage";

    private const string PostgresForeignKeyViolation = "23503";
    private const string PostgresRestrictViolation = "23001";
    private const string SqlServerConstraintConflict = "547";
    private const string SqlServerErrorNumber = "HelpLink.EvtID";
    private const string PostgresConstraintName = "ConstraintName";
    private const string SqliteMarker = "FOREIGN KEY constraint failed";

    private static readonly char[] Quotes = ['\'', '"'];

    /// <summary>
    ///     The validation failure <paramref name="failure" /> stands for, when it is a delete the database
    ///     refused because of a reference the model has a sentence for; otherwise null, and the failure stays
    ///     as it is.
    /// </summary>
    internal static RaskValidationException? Translate(DbContext? context, Exception? failure)
    {
        if (failure is not DbUpdateException { InnerException: DbException provider } update ||
            !IsForeignKeyViolation(provider))
        {
            return null;
        }

        // The entries of the command that failed, not of the whole save: a row inserted with a dangling key
        // beside an unrelated delete is not a refused delete.
        var entries = update.Entries.Where(static entry => entry.State == EntityState.Deleted).ToList();
        var deleted = entries.Select(static entry => entry.Metadata).Distinct().ToList();
        Func<List<IForeignKey>, (bool Asked, IForeignKey? Key)>? ask =
            context is null ? null : keys => Asking(() => ReferenceProbe.Ask(context, keys, entries));

        return deleted.Count > 0 && Refusal(provider, deleted, ask) is { } refusal
            ? new RaskValidationException([refusal], failure)
            : null;
    }

    /// <summary>
    ///     The same for an <c>ExecuteDelete</c> the database refused: <paramref name="command" /> is the statement that
    ///     failed, and says which table it was deleting from.
    /// </summary>
    internal static RaskValidationException? Translate(DbContext? context, DbCommand command, Exception? failure)
    {
        if (context is null ||
            failure is not DbException provider ||
            !IsForeignKeyViolation(provider) ||
            BulkDelete.Parse(command.CommandText) is not { } delete)
        {
            return null;
        }

        var deleted = delete.TypesIn(context.Model);

        return deleted.Count > 0 &&
               Refusal(provider, deleted, keys => Asking(() => ReferenceProbe.Ask(context, keys, command, delete))) is { } refusal
            ? new RaskValidationException([refusal], failure)
            : null;
    }

    // A question that fails leaves the refusal as it would have been without it: never a second error over the first.
    private static (bool Asked, IForeignKey? Key) Asking(Func<(bool Asked, IForeignKey? Key)> ask)
    {
        try
        {
            return ask();
        }
        catch (DbException)
        {
            return (false, null);
        }
        catch (InvalidOperationException)
        {
            return (false, null);
        }
        catch (ArgumentException)
        {
            return (false, null);
        }
    }

    internal static bool IsForeignKeyViolation(DbException provider) =>
        string.Equals(provider.SqlState, PostgresForeignKeyViolation, StringComparison.Ordinal) ||
        string.Equals(provider.SqlState, PostgresRestrictViolation, StringComparison.Ordinal) ||
        string.Equals(provider.Data[SqlServerErrorNumber]?.ToString(), SqlServerConstraintConflict, StringComparison.Ordinal) ||
        provider.Message.Contains(SqliteMarker, StringComparison.Ordinal);

    private static FieldFailure? Refusal(
        DbException provider, List<IEntityType> deleted, Func<List<IForeignKey>, (bool Asked, IForeignKey? Key)>? ask)
    {
        var referencing = deleted.SelectMany(static type => type.GetReferencingForeignKeys()).Distinct().ToList();
        var reported = provider.Data[PostgresConstraintName] as string;

        if (Violated(provider, reported, referencing) is { } violated)
        {
            return Answer(violated, deleted);
        }

        // A constraint was named and the model does not know it — a table that is not mapped — so no
        // relationship's sentence is borrowed for it. Where nothing is named (SQLite) the database is asked which
        // relationship still holds a row, so the answer is the one SQL Server and PostgreSQL give; none of them is a
        // table the model does not map. Only when it cannot be asked is the one relationship that has a sentence
        // taken to be the one that refused.
        if (!NamesItsConstraint(provider))
        {
            var (asked, holding) = ask?.Invoke(ReferenceProbe.Refusing(deleted)) ?? (false, null);
            if (holding is not null)
            {
                return Answer(holding, deleted);
            }

            if (!asked && referencing.Where(static key => SentenceOf(key) is not null).ToList() is [var only])
            {
                return About(SentenceOf(only)!, only.GetConstraintName());
            }
        }

        return OnlySentenceOf(deleted) is var (sentence, type) ? About(sentence, reported ?? type.ShortName()) : null;
    }

    // The database said — or was asked — which relationship: its sentence, or the general one, never another's.
    private static FieldFailure? Answer(IForeignKey violated, List<IEntityType> deleted)
    {
        var principals = deleted.Where(type => violated.PrincipalEntityType.IsAssignableFrom(type)).ToList();
        var message = SentenceOf(violated) ?? OnlySentenceOf(principals)?.Message;

        return message is null ? null : About(message, violated.GetConstraintName());
    }

    private static bool NamesItsConstraint(DbException provider) =>
        provider.Data[PostgresConstraintName] is string || provider.Data[SqlServerErrorNumber] is not null;

    // PostgreSQL reports the name. SQL Server quotes it in its message, beside the database, table and column.
    private static IForeignKey? Violated(DbException provider, string? reported, List<IForeignKey> referencing)
    {
        if (reported is not null)
        {
            return referencing.Find(key => Named(key, reported));
        }

        if (!NamesItsConstraint(provider))
        {
            return null;
        }

        foreach (var quoted in provider.Message.Split(Quotes))
        {
            if (referencing.Find(key => Named(key, quoted)) is { } key)
            {
                return key;
            }
        }

        return null;
    }

    private static bool Named(IForeignKey key, string name) =>
        string.Equals(key.GetConstraintName(), name, StringComparison.Ordinal);

    internal static string? SentenceOf(IReadOnlyForeignKey key) =>
        key.FindAnnotation(RelationshipAnnotation)?.Value as string;

    // The one general sentence among the rows being deleted. Two aggregates deleted in one command, each with
    // a sentence of its own, is a question the error does not answer.
    private static (string Message, IEntityType Type)? OnlySentenceOf(List<IEntityType> deleted)
    {
        var sentences = deleted
            .Select(static type => (Message: GeneralSentenceOf(type), Type: type))
            .Where(static found => found.Message is not null)
            .ToList();

        return sentences is [var only] ? (only.Message!, only.Type) : null;
    }

    internal static string? GeneralSentenceOf(IReadOnlyEntityType type)
    {
        for (var at = type; at is not null; at = at.BaseType)
        {
            if (at.FindAnnotation(EntityAnnotation)?.Value is string sentence)
            {
                return sentence;
            }
        }

        return null;
    }

    // About the delete as a whole: there is no field on a form to put it under.
    internal static FieldFailure About(string message, string? source) => new(message, [], Source: source);
}
