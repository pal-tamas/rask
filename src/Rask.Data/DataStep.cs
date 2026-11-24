using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data;

/// <summary>
///     A migration's data step (<see cref="DataMigrationExtensions.Data" />) as it sits among the migration's operations:
///     a <see cref="SqlOperation" /> whose SQL is a comment, the step held on it as an annotation.
/// </summary>
/// <remarks>
///     A plain <see cref="SqlOperation" /> because every provider's SQL generator writes one, and a type of Rask's own
///     none would — they look an operation up by its exact type. A comment is a statement every database runs as
///     nothing, and a script of the migrations shows where the step sits; the app runs the step itself, in C#, once the
///     schema is migrated (<see cref="DataSteps" />).
/// </remarks>
internal static class DataStep
{
    private const string Annotation = "Rask:DataStep";

    /// <summary>The operation that holds <paramref name="step" />.</summary>
    internal static SqlOperation Holding(Func<Task> step)
    {
        var operation = new SqlOperation
        {
            Sql = "-- A Rask data step: the app runs it in C# once the schema is migrated (rask db migrate, or the app's start).",
        };
        operation.AddAnnotation(Annotation, step);
        return operation;
    }

    /// <summary>The step <paramref name="operation" /> holds, or null for any other operation.</summary>
    internal static Func<Task>? Of(MigrationOperation operation) => operation[Annotation] as Func<Task>;
}
