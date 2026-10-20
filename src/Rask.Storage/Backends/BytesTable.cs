using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Storage.Backends;

/// <summary>
/// The <see cref="StoredFileBytes"/> table as the context's model maps it — its name, its columns and their type
/// mappings — and the two commands EF Core cannot stream: writing a file in, and reading one out.
/// </summary>
/// <remarks>
/// Read off the model rather than assumed, so a table an app created by hand, under its own schema, is addressed exactly
/// as it is mapped. Parameters are made by the columns' own type mappings, so an id is bound the way EF binds it — as
/// text on SQLite, as uuid on PostgreSQL.
/// </remarks>
internal sealed class BytesTable
{
    private const string IdParameter = "id";
    private const string BytesParameter = "bytes";

    private static readonly ConditionalWeakTable<IModel, BytesTable> Tables = new();

    private readonly ISqlGenerationHelper _sql;
    private readonly RelationalTypeMapping _idMapping;
    private readonly RelationalTypeMapping _bytesMapping;
    private readonly string _insert;
    private readonly string _select;

    private BytesTable(DbContext db, IEntityType entity)
    {
        _sql = db.GetService<ISqlGenerationHelper>();
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var id = entity.FindProperty(nameof(StoredFileBytes.Id))!;
        var bytes = entity.FindProperty(nameof(StoredFileBytes.Bytes))!;

        Streams = db.Database.ProviderName is "Microsoft.EntityFrameworkCore.SqlServer" or "Npgsql.EntityFrameworkCore.PostgreSQL";
        _idMapping = id.GetRelationalTypeMapping();
        _bytesMapping = bytes.GetRelationalTypeMapping();

        // Identifiers from the model, delimited by the provider; the values are always parameters.
        var name = _sql.DelimitIdentifier(table.Name, table.Schema);
        var idColumn = _sql.DelimitIdentifier(id.GetColumnName(table)!);
        var bytesColumn = _sql.DelimitIdentifier(bytes.GetColumnName(table)!);
        var idValue = _sql.GenerateParameterNamePlaceholder(IdParameter);
        _insert = $"INSERT INTO {name} ({idColumn}, {bytesColumn}) VALUES ({idValue}, "
            + $"{_sql.GenerateParameterNamePlaceholder(BytesParameter)})";
        _select = $"SELECT {bytesColumn} FROM {name} WHERE {idColumn} = {idValue}";
    }

    /// <summary>The table in <paramref name="db"/>'s model, or a refusal naming the line that maps it.</summary>
    /// <remarks>Worked out once per model, which EF Core itself builds once per context type.</remarks>
    internal static BytesTable Of(DbContext db) =>
        Tables.GetValue(db.Model, model =>
            new BytesTable(db, model.FindEntityType(typeof(StoredFileBytes)) ?? throw NotMapped(db.GetType())));

    /// <summary>
    /// Whether the provider takes a <see cref="Stream"/> as the value of a binary parameter and sends it as it reads it:
    /// SqlClient and Npgsql do. Microsoft.Data.Sqlite binds bytes only, so there the file is read into memory once.
    /// </summary>
    internal bool Streams { get; }

    /// <summary>
    /// <c>INSERT</c> of <paramref name="length"/> bytes: <paramref name="content"/> is a <see cref="Stream"/> where the
    /// provider <see cref="Streams"/>, and the bytes themselves where it does not.
    /// </summary>
    internal DbCommand Insert(DbContext db, Guid id, object content, long length)
    {
        var command = Command(db);
        command.CommandText = _insert;
        command.Parameters.Add(_idMapping.CreateParameter(command, _sql.GenerateParameterName(IdParameter), id, nullable: false));

        var bytes = _bytesMapping.CreateParameter(
            command, _sql.GenerateParameterName(BytesParameter), value: null, nullable: false);

        // The mapping sized the parameter for no value at all — 8000 on SQL Server, which would cut the stream short.
        bytes.Size = checked((int)length);
        bytes.Value = content;
        command.Parameters.Add(bytes);
        return command;
    }

    /// <summary><c>SELECT</c> of one file's bytes, to be read with <see cref="System.Data.CommandBehavior.SequentialAccess"/>.</summary>
    internal DbCommand Select(DbContext db, Guid id)
    {
        var command = Command(db);
        command.CommandText = _select;
        command.Parameters.Add(_idMapping.CreateParameter(command, _sql.GenerateParameterName(IdParameter), id, nullable: false));
        return command;
    }

    internal static InvalidOperationException NotMapped(Type context) =>
        new($"Rask__Storage__Provider is Database, but StoredFileBytes is not in {context.Name}'s model, so there is "
            + "nowhere to keep a file's bytes. Map it with modelBuilder.AddRaskStorage() in OnModelCreating — it maps "
            + "StoredFile and StoredFileBytes together — and create the table: rask db add AddStoredFileBytes && "
            + "rask db update, or by hand in an adopted schema (see docs/file-storage.md).");

    private static DbCommand Command(DbContext db)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        if (db.Database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        return command;
    }
}
