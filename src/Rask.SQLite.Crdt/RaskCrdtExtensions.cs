using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.SQLite.Crdt;

/// <summary>Wires cr-sqlite into an EF Core model and context.</summary>
public static class RaskCrdtExtensions
{
    /// <summary>
    ///     Loads the cr-sqlite extension on every connection this context opens, and finalizes it before
    ///     every close.
    /// </summary>
    /// <remarks>
    ///     Pair with <see cref="ApplyCrdtConventions" /> in <c>OnModelCreating</c> and
    ///     <see cref="PromoteToCrrsAsync" /> once the schema exists. All three are needed: the extension
    ///     alone changes nothing, and promoting a table whose columns lack defaults is refused.
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskCrdt(
        this DbContextOptionsBuilder builder, Action<RaskCrdtOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RaskCrdtOptions();
        configure(options);
        options.Validate();

        return builder.AddInterceptors(new CrdtConnectionInterceptor(options));
    }

    /// <summary>
    ///     Gives every non-key, non-nullable column a SQL default, which cr-sqlite requires.
    ///     Call from <c>OnModelCreating</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         cr-sqlite refuses a table with a <c>NOT NULL</c> column that has no default, because a peer
    ///         running an older schema must be able to apply a change that never mentions that column. EF
    ///         emits exactly that shape for every required property, so an ordinary model is rejected until
    ///         this runs.
    ///     </para>
    ///     <para>
    ///         It sets a default <em>expression</em> rather than a value on purpose. EF suppresses a
    ///         default equal to the CLR default — it cannot tell "unset" from "set to <c>false</c>" — so a
    ///         <c>bool</c> column would silently come out with no default at all, and only that one column
    ///         would fail. Going through the builder matters for the same reason: assignments made
    ///         directly to the metadata do not survive EF's own conventions.
    ///     </para>
    /// </remarks>
    public static ModelBuilder ApplyCrdtConventions(this ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var entity in builder.Model.GetEntityTypes().ToList())
        {
            if (entity.ClrType is null)
            {
                continue;
            }

            var keyNames = entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                           ?? [];
            var entityBuilder = builder.Entity(entity.ClrType);

            foreach (var property in entity.GetProperties().ToList())
            {
                // TryGetDefaultValue, not GetDefaultValue() is not null: for a non-nullable value type the
                // latter hands back the boxed CLR default whether or not one was ever configured, so it
                // reports "already has a default" for every int, bool, Guid and DateTime in the model —
                // i.e. it skips precisely the columns that need one, and leaves only string and byte[]
                // working.
                if (keyNames.Contains(property.Name) ||
                    property.IsNullable ||
                    property.GetDefaultValueSql() is not null ||
                    property.TryGetDefaultValue(out _))
                {
                    continue;
                }

                entityBuilder.Property(property.Name).HasDefaultValueSql(DefaultFor(property));
            }
        }

        return builder;
    }

    /// <summary>
    ///     Promotes tables to conflict-free replicated relations. Run <b>after</b> the schema exists —
    ///     migrations or <c>EnsureCreated</c> — and before any writes worth replicating.
    /// </summary>
    /// <remarks>
    ///     <b>Order matters more than it looks.</b> Loading cr-sqlite seeds its own bookkeeping tables, and
    ///     <c>EnsureCreated</c> treats a database that already has tables as provisioned — so creating the
    ///     schema through a context that loads the extension silently creates nothing at all, and the first
    ///     sign of trouble is this call complaining that a table has no primary key. Create the schema on a
    ///     context without the extension, then promote.
    /// </remarks>
    public static async Task PromoteToCrrsAsync(
        this DbContext context, RaskCrdtOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var table in ResolveTables(context.Model, options))
        {
            // Parameterised: crsql_as_crr takes the table name as a string argument rather than as an
            // identifier, so it binds like any other value.
            await context.Database
                .ExecuteSqlRawAsync("SELECT crsql_as_crr({0});", [table], cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The tables <see cref="PromoteToCrrsAsync" /> will promote: those named in
    ///     <paramref name="options" />, or every table in the model when none are.
    /// </summary>
    /// <remarks>
    ///     Owned types and table splitting mean two entity types can share one table, so the names are
    ///     de-duplicated — promoting the same table twice is not harmless, it is an error.
    /// </remarks>
    internal static IReadOnlyList<string> ResolveTables(IReadOnlyModel model, RaskCrdtOptions? options)
    {
        if (options is { Tables.Count: > 0 })
        {
            return [.. options.Tables.Distinct(StringComparer.Ordinal)];
        }

        return
        [
            .. model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .OfType<string>()
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
        ];
    }

    private static string DefaultFor(IReadOnlyProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (type == typeof(string))
        {
            return "''";
        }

        if (type == typeof(byte[]))
        {
            return "x''";
        }

        if (type == typeof(Guid))
        {
            return "'00000000-0000-0000-0000-000000000000'";
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "'0001-01-01 00:00:00'";
        }

        // Numerics, bool, enums and TimeSpan all store as 0 in SQLite's type system.
        return "0";
    }
}
