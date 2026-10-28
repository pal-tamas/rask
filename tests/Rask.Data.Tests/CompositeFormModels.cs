using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests.Companies;

/// <summary>
/// A company of an existing app, edited through a form: the activities it does are a link table, the columns of its
/// report have fields of their own.
/// </summary>
public sealed class Company : Aggregate<int, Model, PerTenant<int>>
{
    private readonly List<CompanyActivity> _activities = [];
    private readonly List<CompanyColumn> _columns = [];

    public string Name { get; private set; } = "";

    public IReadOnlyList<CompanyActivity> Activities => _activities;

    public IReadOnlyList<CompanyColumn> Columns => _columns;

    public static void Configure(EntityTypeBuilder<Company> builder) =>
        builder.HasMany(c => c.Activities).WithOne().HasForeignKey(a => a.CompanyId);
}

/// <summary>An activity a company can do: one list for every tenant.</summary>
public sealed class Teaor : Aggregate<int, Model>
{
    public string Code { get; private set; } = "";
}

/// <summary>Which activities a company does: the pair and nothing else, in the table the old app made.</summary>
public sealed partial class CompanyActivity : Entity<(Company, Teaor)>
{
    public static void Configure(EntityTypeBuilder<CompanyActivity> builder)
    {
        builder.ToTable("CompanyTeaors");
        builder.HasOne<Teaor>().WithMany().HasForeignKey(a => a.TeaorId);
    }
}

/// <summary>A column of a company's report, keyed by the company and the column it shows.</summary>
public sealed partial class CompanyColumn : Entity<(Company, int ColumnId)>
{
    public int ColumnId { get; private set; }

    public int Order { get; private set; }

    public bool Sum { get; private set; }
}
