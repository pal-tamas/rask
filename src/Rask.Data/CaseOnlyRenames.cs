using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data;

/// <summary>
///     On SQLite, a table or column renamed only in its case — <c>Fuelings</c> to <c>fuelings</c>, <c>Id</c> to
///     <c>id</c>, what Rask's names do to a database EF Core named — renamed in two steps, through a name of its own.
/// </summary>
/// <remarks>
///     SQLite compares names without their case, so <c>ALTER TABLE "Fuelings" RENAME TO "fuelings"</c> is refused: there
///     is already a table of that name. PostgreSQL tells the two apart and SQL Server's <c>sp_rename</c> changes the
///     case, so there the one rename stands.
/// </remarks>
internal static class CaseOnlyRenames
{
    private const string Step = "_rask_renamed";

    /// <summary><paramref name="operations" /> with every case-only rename made in two steps.</summary>
    public static IReadOnlyList<MigrationOperation> Apply(IReadOnlyList<MigrationOperation> operations, string provider)
    {
        if (!string.Equals(provider, ProviderNames.Sqlite, StringComparison.Ordinal) || !operations.Any(IsCaseOnly))
        {
            return operations;
        }

        var result = new List<MigrationOperation>(operations.Count + 4);
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case RenameTableOperation table when IsCaseOnly(table):
                    result.Add(new RenameTableOperation { Name = table.Name, Schema = table.Schema, NewName = table.Name + Step, NewSchema = table.NewSchema });
                    result.Add(new RenameTableOperation { Name = table.Name + Step, Schema = table.NewSchema, NewName = table.NewName, NewSchema = table.NewSchema });
                    break;
                case RenameColumnOperation column when IsCaseOnly(column):
                    result.Add(new RenameColumnOperation { Table = column.Table, Schema = column.Schema, Name = column.Name, NewName = column.Name + Step });
                    result.Add(new RenameColumnOperation { Table = column.Table, Schema = column.Schema, Name = column.Name + Step, NewName = column.NewName });
                    break;
                default:
                    result.Add(operation);
                    break;
            }
        }

        return result;
    }

    private static bool IsCaseOnly(MigrationOperation operation) => operation switch
    {
        RenameTableOperation { NewName: { } to } table => CaseOnly(table.Name, to),
        RenameColumnOperation column => CaseOnly(column.Name, column.NewName),
        _ => false,
    };

    private static bool CaseOnly(string from, string to) =>
        !string.Equals(from, to, StringComparison.Ordinal) && string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
}
