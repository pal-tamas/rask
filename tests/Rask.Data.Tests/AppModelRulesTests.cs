using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data.Tests;

public sealed class Payroll : Aggregate<Guid>
{
    private readonly List<Payslip> _payslips = [];

    public string Title { get; private set; } = "";

    public string Code { get; private set; } = "";

    public string Reference { get; private set; } = "";

    public decimal Total { get; private set; }

    public Guid PayeeId { get; private set; }

    public Guid? AuditorId { get; private set; }

    public IReadOnlyCollection<Payslip> Payslips => _payslips;

    public static void Configure(EntityTypeBuilder<Payroll> builder)
    {
        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasIndex(p => p.Title);
        builder.HasIndex(p => p.Reference).HasDatabaseName("payroll_reference_as_written");
        builder.HasOne<Payee>().WithMany().HasForeignKey(p => p.PayeeId);
        builder.HasOne<Payee>().WithMany().HasForeignKey(p => p.AuditorId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("payroll_auditor_as_written");
    }
}

public sealed class Payslip : Entity<Guid>
{
    public string Memo { get; private set; } = "";
}

public sealed class Payee : Aggregate<Guid>
{
    public string Name { get; private set; } = "";
}

/// <summary>The rules TA's app states once for its whole model — written here exactly as it writes them.</summary>
/// <remarks>
/// Deliberately NOT an <see cref="IRaskModel" />: the generator would name it to the registry for every test in this
/// assembly. The tests name it themselves, and forget it after.
/// </remarks>
internal static class PayrollRules
{
    public static void Conventions(ModelConfigurationBuilder c)
    {
        c.Properties<string>().HaveMaxLength(1000);
        c.Properties<decimal>().HaveColumnType("decimal(32, 16)");
        c.Conventions.Remove(typeof(ForeignKeyIndexConvention));
        c.ConstraintNames(n => n.Kind switch
        {
            ConstraintKind.PrimaryKey => $"{n.Table}_pkey",
            ConstraintKind.ForeignKey => $"{n.Table}_{n.Columns[0]}_fkey",
            ConstraintKind.Unique => $"{n.Table}_{n.Joined}_key",
            ConstraintKind.Index => $"{n.Table}_{n.Joined}_idx",
            ConstraintKind.Default => $"{n.Table}_{n.Columns[0]}_df",
            _ => null,
        });
        c.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);
    }

    public static void Model(ModelBuilder m) => m.HasDefaultSchema("dbo");
}

/// <summary>A context of the app's own, deriving from Rask's: the model it builds goes through the registry.</summary>
public sealed class PayrollContext(DbContextOptions<PayrollContext> options) : RaskDbContext(options);

/// <summary>The design-time model of <see cref="PayrollContext" />, built once under <see cref="PayrollRules" />.</summary>
public sealed class PayrollModelFixture
{
    public PayrollModelFixture()
    {
        ModelRegistry.UseModel(typeof(PayrollRules), PayrollRules.Conventions, PayrollRules.Model);
        try
        {
            using var context = new PayrollContext(
                new DbContextOptionsBuilder<PayrollContext>().UseSqlite("Data Source=:memory:").Options);
            Model = context.GetService<IDesignTimeModel>().Model;
        }
        finally
        {
            ModelRegistry.ForgetModel();
        }
    }

    public IModel Model { get; }
}

/// <summary>Alone, after every parallel class: while the rules are named, every model built anywhere gets them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppModelRulesCollection : ICollectionFixture<PayrollModelFixture>
{
    public const string Name = "app-model-rules";
}

/// <summary>An app's <see cref="IRaskModel" /> reaches every table its context maps, after Rask's rules and before none of the entity's own.</summary>
[Collection(AppModelRulesCollection.Name)]
public sealed class AppModelRulesTests(PayrollModelFixture fixture)
{
    private IEntityType Payroll => fixture.Model.FindEntityType(typeof(Payroll))!;

    private IEntityType Payslip => fixture.Model.FindEntityType(typeof(Payslip))!;

    [Fact]
    public void Every_string_and_decimal_is_mapped_under_the_app_s_conventions()
    {
        var title = Payroll.FindProperty(nameof(Rask.Data.Tests.Payroll.Title))!;

        var total = Payroll.FindProperty(nameof(Rask.Data.Tests.Payroll.Total))!;

        Assert.Equal(1000, title.GetMaxLength());
        Assert.Equal(1000, Payslip.FindProperty(nameof(Rask.Data.Tests.Payslip.Memo))!.GetMaxLength());
        Assert.Equal("decimal(32, 16)", total.GetColumnType());
    }

    [Fact]
    public void Every_key_index_and_foreign_key_is_named_by_the_app_s_rule()
    {
        var table = Payroll.GetTableName();
        var payslips = Payslip.GetTableName();

        var childKey = Payslip.GetForeignKeys().Single();

        Assert.Equal($"{table}_pkey", Payroll.FindPrimaryKey()!.GetName());
        Assert.Equal($"{table}_Code_key", Payroll.GetIndexes().Single(i => i.IsUnique).GetDatabaseName());
        Assert.Equal($"{table}_Title_idx", IndexOn(nameof(Rask.Data.Tests.Payroll.Title)).GetDatabaseName());
        Assert.Equal($"{table}_PayeeId_fkey", KeyOn(nameof(Rask.Data.Tests.Payroll.PayeeId)).GetConstraintName());
        Assert.Equal($"{payslips}_{childKey.Properties[0].GetColumnName()}_fkey", childKey.GetConstraintName());
    }

    [Fact]
    public void A_name_the_entity_states_itself_wins_over_the_rule()
    {
        var reference = IndexOn(nameof(Rask.Data.Tests.Payroll.Reference));

        var auditor = KeyOn(nameof(Rask.Data.Tests.Payroll.AuditorId));

        Assert.Equal("payroll_reference_as_written", reference.GetDatabaseName());
        Assert.Equal("payroll_auditor_as_written", auditor.GetConstraintName());
    }

    [Fact]
    public void Children_cascade_on_the_client_and_every_other_key_is_no_action_unless_it_says_otherwise()
    {
        var child = Payslip.GetForeignKeys().Single();

        var payee = KeyOn(nameof(Rask.Data.Tests.Payroll.PayeeId));
        var auditor = KeyOn(nameof(Rask.Data.Tests.Payroll.AuditorId));

        Assert.Equal(DeleteBehavior.ClientCascade, child.DeleteBehavior);
        Assert.True(child.IsRequired);
        Assert.Equal(DeleteBehavior.NoAction, payee.DeleteBehavior);
        Assert.Equal(DeleteBehavior.SetNull, auditor.DeleteBehavior);
    }

    [Fact]
    public void A_convention_the_app_removes_stays_removed()
    {
        var payslipIndexes = Payslip.GetIndexes();

        var payrollIndexed = Payroll.GetIndexes().SelectMany(i => i.Properties).Select(p => p.Name).ToList();

        Assert.Empty(payslipIndexes);
        Assert.DoesNotContain(nameof(Rask.Data.Tests.Payroll.PayeeId), payrollIndexed);
    }

    [Fact]
    public void The_model_hook_reaches_the_whole_model()
    {
        var schemas = fixture.Model.GetEntityTypes().Where(e => e.GetTableName() is not null).Select(e => e.GetSchema());

        Assert.All(schemas, schema => Assert.Equal("dbo", schema));
    }

    [Fact]
    public void A_second_model_class_is_refused_naming_both()
    {
        ModelRegistry.UseModel(typeof(PayrollRules), PayrollRules.Conventions, PayrollRules.Model);

        try
        {
            var refused = Assert.Throws<InvalidOperationException>(() =>
                ModelRegistry.UseModel(typeof(PayrollModelFixture), static _ => { }, static _ => { }));

            Assert.Contains(typeof(PayrollRules).FullName!, refused.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(PayrollModelFixture).FullName!, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            ModelRegistry.ForgetModel();
        }
    }

    private IIndex IndexOn(string property) =>
        Payroll.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == property);

    private IForeignKey KeyOn(string property) =>
        Payroll.GetForeignKeys().Single(k => k.Properties[0].Name == property);
}
