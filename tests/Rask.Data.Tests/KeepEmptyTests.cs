using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// A text kept empty (<c>KeepEmpty</c>) is written as '' where its field is left empty, never as NULL — and a row that
/// holds NULL there keeps it until somebody fills the field.
/// </summary>
public sealed class KeepEmptyTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_new_row_stores_an_empty_text_kept_empty_as_empty_and_one_not_kept_as_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var created = await Firm.Create(new FirmModel { Vat = null, GroupVat = "", Note = null }, cancellationToken: token);

        var stored = await database.Load<Firm>(created.Id, token);

        Assert.Equal("", stored!.Vat!.Value);
        Assert.Equal("", stored.Note);
        Assert.Null(stored.GroupVat);
        Assert.Equal(["", null, ""], await ColumnsAsync(database, created.Id));
    }

    [Fact]
    public async Task An_emptied_text_kept_empty_becomes_empty_while_a_stored_null_left_empty_stays_null()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var filled = await Firm.Create(new FirmModel { Vat = "HU12345678", GroupVat = "HU87654321", Note = "kept" }, cancellationToken: token);
        var old = await Firm.Create(new FirmModel(), cancellationToken: token);
        await WriteNullsAsync(database, old.Id);

        await Firm.Update(filled.Id, new FirmModel { Vat = "  ", GroupVat = null, Note = null }, cancellationToken: token);
        await Firm.Update(old.Id, new FirmModel { Vat = null, Note = null }, cancellationToken: token);

        Assert.Equal(["", null, ""], await ColumnsAsync(database, filled.Id));
        Assert.Equal([null, null, null], await ColumnsAsync(database, old.Id));
    }

    [Fact]
    public async Task A_filled_text_kept_empty_still_answers_to_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Firm.Create(new FirmModel { Vat = "nonsense" }, cancellationToken: token));
        var created = await Firm.Create(new FirmModel { Vat = "HU12345678" }, cancellationToken: token);

        Assert.Equal(["Vat"], refused.Failures.Select(f => f.Fields.Single()));
        Assert.Equal("HU12345678", (await database.Load<Firm>(created.Id, token))!.Vat!.Value);
    }

    [Fact]
    public async Task A_stored_empty_text_loads_as_the_value_object_holding_it_and_fills_the_model_with_it()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var created = await Firm.Create(new FirmModel(), cancellationToken: token);

        var model = await Firm.Model(created.Id, cancellationToken: token);
        await Firm.Update(created.Id, model!, cancellationToken: token);

        Assert.Equal("", model!.Vat);
        Assert.Equal("", model.Note);
        Assert.Equal(["", null, ""], await ColumnsAsync(database, created.Id));
    }

    [Fact]
    public void KeepEmpty_marks_a_text_column_and_refuses_one_that_is_not_text()
    {
        var builder = new ModelBuilder();
        var plain = builder.Entity<Plain>();

        plain.KeepEmpty(p => p.Name);
        var refused = Assert.Throws<InvalidOperationException>(() => plain.KeepEmpty(p => p.Count));

        Assert.True((bool)plain.Metadata.FindProperty(nameof(Plain.Name))!.FindAnnotation("Rask:KeepEmpty")!.Value!);
        Assert.Contains("'Plain.Count' is stored as a Int32", refused.Message, StringComparison.Ordinal);
    }

    // The columns as stored, read raw so '' and NULL stay apart: Vat, GroupVat, Note.
    private static async Task<string?[]> ColumnsAsync(TestDatabase database, int id)
    {
        await database.Context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = database.Context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Vat\", \"GroupVat\", \"Note\" FROM \"{Table(database)}\" WHERE \"Id\" = {id}";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return [.. Enumerable.Range(0, 3).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i))];
    }

    // An old row, stored before the form wrote '': NULL in every text.
    private static async Task WriteNullsAsync(TestDatabase database, int id)
    {
#pragma warning disable EF1002 // the table name comes from the model, the id is a parameter
        await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{Table(database)}\" SET \"Vat\" = NULL, \"GroupVat\" = NULL, \"Note\" = NULL WHERE \"Id\" = {{0}}",
            [id], TestContext.Current.CancellationToken);
#pragma warning restore EF1002
    }

    private static string Table(TestDatabase database) => database.Context.Model.FindEntityType(typeof(Firm))!.GetTableName()!;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));

    private sealed class Plain
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public int Count { get; set; }
    }
}
