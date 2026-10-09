using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Core.Forms;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data.Tests;

/// <summary>
/// The unique rules an aggregate declares, asked about its form model BEFORE the save — what a form bound to
/// the generated model does by itself. Asked here through the very thing the generator announced.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class StoreRulesBeforeTheSaveTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-store-rules-{Guid.NewGuid():N}.db");
    private readonly ContactQueries _queries = new();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_taken_name_is_told_for_its_field_and_for_the_whole_model_and_a_free_one_is_not()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));

        var field = await CheckAsync(Acme, new ContactModel { Name = "Ada" }, "Name");
        var whole = await CheckAsync(Acme, new ContactModel { Name = "Ada" }, null);
        var free = await CheckAsync(Acme, new ContactModel { Name = "Bob" }, null);

        var failure = Assert.Single(field);
        Assert.Equal(Contact.NameTaken, failure.Message);
        Assert.Equal(["Name"], failure.Fields);
        Assert.Equal("IX_Contacts_Name_TenantId", failure.Source);
        Assert.Equal(Contact.NameTaken, Assert.Single(whole).Message);
        Assert.Empty(free);
    }

    [Fact]
    public async Task A_name_another_tenant_holds_is_not_seen_and_is_never_asked_about_with_no_tenant_or_across_them()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Globex, Contact.Named("Ada"));
        var rules = Rules()!;

        var acme = await CheckAsync(Acme, new ContactModel { Name = "Ada" }, null);
        _queries.Count = 0;
        IReadOnlyList<FieldFailure> across;
        using (Tenant.Across())
        {
            across = await rules.Check(new ContactModel { Name = "Ada" }, null, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<FieldFailure> nobody;
        using (Tenant.None())
        {
            nobody = await rules.Check(new ContactModel { Name = "Ada" }, null, TestContext.Current.CancellationToken);
        }

        // Acme's form learns nothing about Globex's rows; and with no single tenant the rule is left to the save.
        Assert.Empty(acme);
        Assert.Empty(across);
        Assert.Empty(nobody);
        Assert.Equal(0, _queries.Count);
    }

    [Fact]
    public async Task The_row_a_model_was_filled_from_does_not_collide_with_itself_but_does_with_another()
    {
        await using var database = await StartDatabaseAsync();
        var ada = await SaveAsync(Acme, Contact.Named("Ada"));
        await SaveAsync(Acme, Contact.Named("Bob"));
        ContactModel edited;
        using (Tenant.Use(Acme))
        {
            edited = (await Contact.Model(ada, cancellationToken: TestContext.Current.CancellationToken))!;
        }

        var unchanged = await CheckAsync(Acme, edited, null);
        edited.Name = "Bob";
        var renamedOntoAnother = await CheckAsync(Acme, edited, null);

        Assert.Empty(unchanged);
        Assert.Equal(Contact.NameTaken, Assert.Single(renamedOntoAnother).Message);
    }

    [Fact]
    public async Task A_forged_key_only_hides_a_row_from_the_check_before_the_save_and_the_save_still_refuses()
    {
        await using var database = await StartDatabaseAsync();
        var ada = await SaveAsync(Acme, Contact.Named("Ada"));
        var forged = new ContactModel { Name = "Ada", __Key = ada };

        var before = await CheckAsync(Acme, forged, null);
        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => Contact.Create(forged, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Empty(before);
        Assert.Equal(Contact.NameTaken, Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task A_field_is_asked_only_about_the_rules_that_name_it()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada", code: "C-1"));
        var model = new ContactModel { Name = "Ada", Code = "C-1", Rank = 3 };

        var name = await CheckAsync(Acme, model, "Name");
        var code = await CheckAsync(Acme, model, "Code");
        var rank = await CheckAsync(Acme, model, "Rank");
        var whole = await CheckAsync(Acme, model, null);

        Assert.Equal(["Name"], Assert.Single(name).Fields);
        Assert.Equal(Contact.CodeTaken, Assert.Single(code).Message);
        Assert.Empty(rank);
        Assert.Equal(2, whole.Count);
    }

    [Fact]
    public async Task A_field_not_filled_in_yet_asks_nothing()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        _queries.Count = 0;

        var failures = await CheckAsync(Acme, new ContactModel { Name = null, Code = null }, null);

        Assert.Empty(failures);
        Assert.Equal(0, _queries.Count);
    }

    [Fact]
    public async Task A_rule_already_answered_for_these_values_is_not_asked_again_until_one_changes_or_the_form_submits()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        var model = new ContactModel { Name = "Ada" };
        _queries.Count = 0;

        var first = await CheckAsync(Acme, model, "Name");
        var again = await CheckAsync(Acme, model, "Name");
        var afterTwo = _queries.Count;
        model.Name = "Bob";
        await CheckAsync(Acme, model, "Name");
        var afterAChange = _queries.Count;
        await CheckAsync(Acme, model, null);
        var afterASubmit = _queries.Count;

        Assert.Equal(first.Select(f => f.Message), again.Select(f => f.Message));
        Assert.Single(first);
        Assert.Equal(1, afterTwo);
        Assert.Equal(2, afterAChange);
        Assert.Equal(3, afterASubmit);
    }

    [Fact]
    public async Task A_cancelled_check_stops_instead_of_answering()
    {
        await using var database = await StartDatabaseAsync();
        var rules = Rules()!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var check = async () => await rules.Check(new ContactModel { Name = "Ada" }, null, cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(check);
    }

    [Fact]
    public void With_no_database_to_ask_there_are_no_rules_and_the_form_asks_nothing()
    {
        Db.Reset();

        var rules = Rules();

        Assert.Null(rules);
    }

    [Fact]
    public void Only_a_generated_model_whose_aggregate_declares_a_unique_rule_is_announced_to_forms()
    {
        var generated = RaskValidation.HasStoreRulesFor(typeof(ContactModel));
        var handWritten = RaskValidation.HasStoreRulesFor(typeof(ComplexContact));

        Assert.True(generated);
        Assert.False(handWritten);
    }

    private static async Task<IReadOnlyList<FieldFailure>> CheckAsync(int tenant, ContactModel model, string? field)
    {
        using (Tenant.Use(tenant))
        {
            return await Rules()!.Check(model, field, TestContext.Current.CancellationToken);
        }
    }

    // What the generator registered for ContactModel, reached the only way a file-local class can be: by name.
    private static IStoreRules? Rules()
    {
        var rules = typeof(ContactModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__ContactModelRules", StringComparison.Ordinal));
        var method = rules.GetMethod("StoreRules", BindingFlags.Static | BindingFlags.NonPublic)!;
        return method.CreateDelegate<Func<IServiceProvider?, IStoreRules?>>()(null);
    }

    private static async Task<int> SaveAsync(int tenant, Contact contact)
    {
        using (Tenant.Use(tenant))
        {
            await contact.Save(cancellationToken: TestContext.Current.CancellationToken);
            return contact.Id;
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_queries));

    /// <summary>Counts the reads of the contacts table.</summary>
    private sealed class ContactQueries : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Contacts\"", StringComparison.Ordinal))
            {
                Count++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
