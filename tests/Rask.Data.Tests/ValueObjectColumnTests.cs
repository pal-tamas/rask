using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object the usual way: a text declared from its base record, with a rule of its own.</summary>
public sealed partial record ContactName(string Value) : Text(Value)
{
    public const string TooShort = "A name has at least two characters.";

    // The rule, where the value is: asked as it is made, and by every form and write over the model. A blank
    // name is the base's to refuse, as required.
    protected override IEnumerable<Problem> Rules()
    {
        if (Value.Length is > 0 and < 2)
        {
            yield return Problem.Whole(TooShort);
        }
    }
}

/// <summary>The shape RASK084 asks for: nothing public to set, built through a private constructor.</summary>
public sealed record ContactCode
{
    private ContactCode() { }

    public string Value { get; private set; } = "";

    public static ContactCode Of(string value) => new() { Value = value };
}

/// <summary>A one-value value object that is a struct, with no rule of its own.</summary>
public readonly record struct ContactRank(int Value);

/// <summary>An adopted table whose name is a value object — and is what the unique rule is over.</summary>
public sealed class Contact : Aggregate<int, Model, Deletable, PerTenant>
{
    public const string NameTaken = "Ilyen néven már van névjegy.";
    public const string CodeTaken = "This code is already in use.";

    private Contact() { }

    public int? TenantId { get; private set; }

    public ContactName Name { get; private set; } = null!;

    public ContactCode? Code { get; private set; }

    public ContactRank Rank { get; private set; }

    public static Contact Named(string name, string? code = null, int rank = 0) =>
        new() { Name = ContactName.Create(name), Code = code is null ? null : ContactCode.Of(code), Rank = new ContactRank(rank) };

    public void Rename(string name) => Name = ContactName.Create(name);

    public static void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(255);
        builder.HasIndex(c => new { c.Name, c.TenantId }).IsUnique(NameTaken);
        builder.HasIndex(c => c.Code).IsUnique(CodeTaken);
    }
}

/// <summary>The same table as a one-value value object used to be mapped, for the columns to be compared with.</summary>
public sealed class ComplexContact
{
    public int Id { get; set; }

    public int? TenantId { get; set; }

    public ContactName Name { get; set; } = ContactName.Create("--");

    public ContactCode? Code { get; set; }

    public ContactRank Rank { get; set; }
}

public sealed class ComplexContactContext(DbContextOptions<ComplexContactContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ComplexContact>(e =>
        {
            e.ToTable("Contacts");
            e.ComplexProperty(x => x.Name, b => b.Property(v => v.Value).HasColumnName("Name").HasMaxLength(255));
            e.ComplexProperty(x => x.Code, b => b.Property(v => v.Value).HasColumnName("Code"));
            e.ComplexProperty(x => x.Rank, b => b.Property(v => v.Value).HasColumnName("Rank"));
        });
}

/// <summary>
/// A value object that holds one value is stored as that value's column — a converted scalar — so it can be
/// indexed and made unique like any other column, and the table is the one the old complex mapping produced.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ValueObjectColumnTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-vo-column-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_value_object_is_one_scalar_column_named_after_the_property_with_its_own_facets()
    {
        await using var database = await StartDatabaseAsync();

        var name = database.Context.Model.FindEntityType(typeof(Contact))!.FindProperty(nameof(Contact.Name))!;

        Assert.Equal("Name", name.GetColumnName());
        Assert.Equal(typeof(string), name.GetValueConverter()!.ProviderClrType);
        Assert.Equal(255, name.GetMaxLength());
        Assert.False(name.IsNullable);
    }

    [Fact]
    public async Task The_table_has_the_columns_the_complex_mapping_gave_it()
    {
        await using var database = await StartDatabaseAsync();
        using var complex = new ComplexContactContext(OptionsLayout.LikeAddDbContext<ComplexContactContext>().UseSqlite("Data Source=:memory:").Options);

        var now = ColumnsOf(database.Context);
        var before = ColumnsOf(complex);

        Assert.Equal(before, now);
    }

    [Fact]
    public async Task Every_shape_of_one_value_type_is_saved_and_read_back_whole()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada", code: "A-1", rank: 3));
        var bare = await SaveAsync(Acme, Contact.Named("Bob"));

        Contact ada, bob;
        using (Tenant.Use(Acme))
        {
            ada = (await Contact.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;
            bob = (await Contact.Find(bare, cancellationToken: TestContext.Current.CancellationToken))!;
        }

        Assert.Equal(ContactName.Create("Ada"), ada.Name);
        Assert.Equal("A-1", ada.Code!.Value);
        Assert.Equal(new ContactRank(3), ada.Rank);
        Assert.Null(bob.Code);
    }

    [Fact]
    public async Task An_index_over_the_value_objects_column_builds_and_keeps_two_tenants_apart()
    {
        await using var database = await StartDatabaseAsync();
        var index = database.Context.Model.FindEntityType(typeof(Contact))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["Name", "TenantId"]));
        await SaveAsync(Acme, Contact.Named("Ada"));

        await SaveAsync(Globex, Contact.Named("Ada"));

        Assert.True(index.IsUnique);
        using (Tenant.Across())
        {
            Assert.Equal(2, await Contact.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_duplicate_is_refused_with_the_message_under_the_property_not_under_its_inner_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Contact.NameTaken, failure.Message);
        Assert.Equal(["Name"], failure.Fields);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task A_nullable_value_object_collides_only_when_it_has_a_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        await SaveAsync(Acme, Contact.Named("Bob"));
        await SaveAsync(Acme, Contact.Named("Cy", code: "C-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Di", code: "C-1")));

        Assert.Equal(["Code"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task The_declared_rule_is_asked_as_a_query_over_the_value_objects_column_when_the_index_is_gone()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<DomainContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<DomainContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Pooling=False"));
        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);
        try
        {
            await using var context = await app.GetRequiredService<IDbContextFactory<DomainContext>>()
                .CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlAsync(
                $"DROP INDEX \"IX_Contacts_Name_TenantId\"", TestContext.Current.CancellationToken);
            await SaveAsync(Acme, Contact.Named("Ada"));

            var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Ada")));
            await SaveAsync(Globex, Contact.Named("Ada"));

            Assert.Equal(["Name"], Assert.Single(refused.Failures).Fields);
            Assert.Null(refused.InnerException);
        }
        finally
        {
            Db.Reset();
        }
    }

    [Fact]
    public async Task The_read_face_filters_and_sorts_by_the_value_as_the_primitive_it_is()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Adam", rank: 2));
        await SaveAsync(Acme, Contact.Named("Ada", rank: 9));
        await SaveAsync(Acme, Contact.Named("Bob", rank: 5));

        List<string> names;
        using (Tenant.Use(Acme))
        {
            names = await Contact.Where(c => c.Name.StartsWith("Ada") && c.Rank > 1).OrderBy(c => c.Name).Select(c => c.Name);
        }

        Assert.Equal(["Ada", "Adam"], names);
    }

    [Fact]
    public async Task On_the_write_model_the_whole_value_and_the_value_inside_it_both_compare_in_sql()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        var wanted = ContactName.Create("Ada");

        using (Tenant.Use(Acme))
        {
            var whole = await database.Context.Set<Contact>().AsNoTracking()
                .Where(c => c.Name == wanted).ToListAsync(TestContext.Current.CancellationToken);
            var inside = await database.Context.Set<Contact>().AsNoTracking()
                .Where(c => c.Name.Value.Contains("d")).ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(whole);
            Assert.Single(inside);
        }
    }

    [Fact]
    public async Task The_edit_form_carries_the_value_and_a_rename_writes_it_back()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada"));

        using (Tenant.Use(Acme))
        {
            var model = (await Contact.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
            model.Name = "Adele";
            await Contact.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);
            await Contact.Update(id, c => c.Rename("Adeline"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["Adeline"], await Contact.Select(c => c.Name));
        }
    }

    [Fact]
    public async Task A_model_filled_from_a_row_remembers_which_row_and_a_new_model_remembers_none()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada"));

        ContactModel filled;
        using (Tenant.Use(Acme))
        {
            filled = (await Contact.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        }

        Assert.Equal(id, filled.__Key);
        Assert.Null(new ContactModel().__Key);
    }

    [Fact]
    public void The_row_a_model_was_filled_from_is_not_a_field_a_form_could_bind_or_a_client_could_post()
    {
        var key = typeof(ContactModel).GetProperty("__Key", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var publicNames = typeof(ContactModel).GetProperties().Select(p => p.Name);

        Assert.NotNull(key);
        Assert.DoesNotContain("__Key", publicNames);
    }

    [Theory]
    [InlineData("Name", typeof(Rask.Core.Forms.Validate<string>))]
    [InlineData("Rank", typeof(Rask.Core.Forms.Validate<int?>))]
    public void A_value_objects_rule_is_one_kept_delegate_of_the_fields_own_type_handed_back_every_time(string property, Type shape)
    {
        var ruleOf = RuleOfContactModel();

        var first = ruleOf(property);
        var second = ruleOf(property);

        Assert.IsType(shape, first);
        Assert.Same(first, second);
    }

    [Fact]
    public void A_property_whose_type_states_no_rule_has_none()
    {
        var ruleOf = RuleOfContactModel();

        var code = ruleOf("Code");
        var unknown = ruleOf("Nothing");

        Assert.Null(code);
        Assert.Null(unknown);
    }

    [Fact]
    public void A_name_that_cannot_be_null_is_asked_its_own_rule_and_is_required_in_the_bases_words()
    {
        var rule = (Rask.Core.Forms.Validate<string>)RuleOfContactModel()("Name")!;

        var tooShort = rule("a").ToList();
        var fine = rule("Ada").ToList();
        var nothing = rule(null!).ToList();

        Assert.Equal([ContactName.TooShort], tooShort);
        Assert.Empty(fine);
        Assert.Equal([GeneratedModelRules.RequiredMessage], nothing);
    }

    [Fact]
    public void A_number_that_cannot_be_null_and_holds_nothing_says_the_one_sentence_since_there_is_no_empty_number_to_ask_about()
    {
        var rule = (Rask.Core.Forms.Validate<int?>)RuleOfContactModel()("Rank")!;

        var nothing = rule(null).ToList();
        var negative = rule(-1).ToList();

        Assert.Equal([GeneratedModelRules.RequiredMessage], nothing);
        Assert.Empty(negative);
    }

    [Fact]
    public async Task Create_and_update_ask_the_same_rules_with_no_form_and_refuse_before_any_query()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada", rank: 1));
        var commands = new CommandCount();
        using var listening = commands.Listen();
        var model = new ContactModel { Name = "", Rank = null };

        RaskValidationException create;
        RaskValidationException update;
        using (Tenant.Use(Acme))
        {
            create = await Assert.ThrowsAsync<RaskValidationException>(
                () => Contact.Create(model, cancellationToken: TestContext.Current.CancellationToken));
            update = await Assert.ThrowsAsync<RaskValidationException>(
                () => Contact.Update(id, new ContactModel { Name = "x", Rank = -5 }, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            [("Name", GeneratedModelRules.RequiredMessage), ("Rank", GeneratedModelRules.RequiredMessage)],
            create.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal([("Name", ContactName.TooShort)], update.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal(0, commands.Count);
    }

    [Fact]
    public async Task A_model_the_rules_accept_is_written_as_before()
    {
        await using var database = await StartDatabaseAsync();

        Contact? stored;
        using (Tenant.Use(Acme))
        {
            var created = await Contact.Create(new ContactModel { Name = "Ada", Rank = 2 }, cancellationToken: TestContext.Current.CancellationToken);
            stored = await database.Load<Contact>(created.Id, TestContext.Current.CancellationToken);
        }

        Assert.Equal(ContactName.Create("Ada"), stored!.Name);
        Assert.Equal(new ContactRank(2), stored.Rank);
    }

    // The generated switch itself, reached the only way a file-local class can be: by its name.
    private static Func<string, Delegate?> RuleOfContactModel()
    {
        var rules = typeof(ContactModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__ContactModelRules", StringComparison.Ordinal));
        var method = rules.GetMethod("Root_RuleOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return method.CreateDelegate<Func<string, Delegate?>>();
    }

    private static List<string> ColumnsOf(DbContext context) =>
        [.. context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == "Contacts").Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name} {c.StoreType} {(c.IsNullable ? "NULL" : "NOT NULL")}")];

    /// <summary>Counts every command EF Core sends while it listens.</summary>
    private sealed class CommandCount : IObserver<System.Diagnostics.DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IDisposable Listen() => System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this);

        public void OnNext(System.Diagnostics.DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuting.Name)
            {
                Interlocked.Increment(ref _count);
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
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
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
