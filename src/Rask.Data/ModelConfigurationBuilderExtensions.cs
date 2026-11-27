using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>The app-wide model rules an <see cref="IRaskModel" /> states in its <c>Conventions</c>.</summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    ///     Names every primary key, foreign key, unique constraint, index, check and (on SQL Server) default the model did
    ///     not name explicitly, in place of Rask's <c>pk_</c>/<c>fk_</c>/<c>uq_</c>/<c>ix_</c>/<c>ck_</c>/<c>df_</c> names:
    ///     <c>c.ConstraintNames(n =&gt; $"{n.Table}_{n.Joined}_idx")</c>.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="name">
    ///     The name for one constraint — <c>n.Prefixed</c> is Rask's — or null to keep EF Core's. A name longer than the
    ///     provider allows is cut to its limit and ends in a hash of the whole name.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     Applied as the model is finalized, after every entity's own <c>Configure</c>: a <c>HasDatabaseName</c> or
    ///     <c>HasConstraintName</c> there wins. In a Rask context it names the constraints after every other convention,
    ///     wherever in <c>Conventions</c> it is called.
    /// </remarks>
    public static ModelConfigurationBuilder ConstraintNames(
        this ModelConfigurationBuilder configurationBuilder, Func<Constraint, string?> name)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(name);

        var naming = StoreNameConvention.For(configurationBuilder);
        naming.Constraints = name;
        if (!naming.NamesConstraintsLast)
        {
            naming.NameConstraints(configurationBuilder);
        }

        return configurationBuilder;
    }

    /// <summary>
    ///     How the database compares text, named once by its SQL Server collation: <c>c.OrderTextBy("Hungarian_100_CI_AS")</c>
    ///     — and <c>OrderBy(x =&gt; x.Name.InTextOrder())</c> puts <c>Cu</c> before <c>Cs</c> and <c>O</c> before <c>Ö</c>, as
    ///     Hungarian does.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="collation">The SQL Server collation the app's database compares text by.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         On SQL Server only the <c>ORDER BY</c> changes: <c>x.Name.InTextOrder()</c> is written
    ///         <c>[Name] COLLATE Hungarian_100_CI_AS</c>, no column is given the collation, and a <c>Where</c> compares as
    ///         the database's own default collation says.
    ///     </para>
    ///     <para>
    ///         On SQLite through <c>UseRaskSqlite</c> (or a browser's <c>UseSqlite(…).UseRaskFullTextSearch()</c>) the
    ///         database compares as SQL Server would: every connection registers
    ///         the collation's language and sensitivities as <c>RASK_TEXT</c>, every text column is created with it, and
    ///         <c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c> and <c>LIKE</c> compare by it too — so <c>"csaba"</c>
    ///         finds <c>Csaba</c>, a unique index refuses <c>CSABA</c> beside it, and <c>"árv"</c> is in <c>Árvíztűrő</c>.
    ///         A <c>UseSqlite</c> with nothing of Rask's orders by the column as it is.
    ///     </para>
    ///     <para>
    ///         On PostgreSQL 18 or later through <c>UseRaskPostgres</c> a collation that ignores case or accents becomes
    ///         a nondeterministic ICU collation, <c>rask_text</c>, which the migrations create and every text column is
    ///         created with — so there too equality, a unique index and <c>LIKE</c> ignore case; an older server stops the
    ///         migration with the reason. A plain <c>UseNpgsql</c>, or a collation that tells case and accents apart,
    ///         orders by the language's ICU collation (<c>"hu-HU-x-icu"</c>) alone. Order a list already read with
    ///         <c>TextOrder.In("hu")</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Off SQL Server, as the model is built: <paramref name="collation" /> names a language Rask cannot tell.
    /// </exception>
    public static ModelConfigurationBuilder OrderTextBy(this ModelConfigurationBuilder configurationBuilder, string collation)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collation);

        configurationBuilder.Conventions.Add(services => TextOrderTranslationConvention.For(collation, services));
        return configurationBuilder;
    }

    /// <summary>
    ///     A foreign key from every <see cref="PerTenant" /> table's <c>TenantId</c> to the tenant's own key:
    ///     <c>c.TenantReferences&lt;Partner&gt;()</c> — so the database refuses to delete a tenant that still has rows, on
    ///     SQLite as on SQL Server and PostgreSQL.
    /// </summary>
    /// <typeparam name="TTenant">The app's tenant aggregate, the one that says <see cref="ITenant" />.</typeparam>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         The key is the database's, made by the migrations and by <c>EnsureCreated</c> through <c>UseRaskSqlite</c>,
    ///         <c>UseRaskSqlServer</c> and <c>UseRaskPostgres</c>: <c>NO ACTION</c> on delete, named by the app's
    ///         <c>ConstraintNames</c> rule (<see cref="ConstraintKind.ForeignKey" />), Rask's <c>fk_{table}_tenant_id</c> by default.
    ///         It is no relationship of the model — EF Core relates only properties of one type, and a tenant is often a
    ///         <see cref="Guid" /> to its tables and text to its own row — so a save neither orders its commands by it nor
    ///         deletes a tenant's rows with it. Give the tenant a <c>WhenReferenced("…")</c> sentence and a refused delete
    ///         says it.
    ///     </para>
    ///     <para>
    ///         SQLite makes a foreign key only with its table, so there it is in the <c>CREATE TABLE</c> — a database
    ///         made from the model, a test's — and a migration that adds it to a table SQLite already has adds nothing.
    ///     </para>
    /// </remarks>
    public static ModelConfigurationBuilder TenantReferences<TTenant>(this ModelConfigurationBuilder configurationBuilder)
        where TTenant : class
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Add(_ => new TenantReferenceConvention(typeof(TTenant).FullName!));
        return configurationBuilder;
    }

    /// <summary>
    ///     What a delete does, for every relationship the model left to convention: <paramref name="children" /> for an
    ///     aggregate's children, <paramref name="others" /> for every other foreign key.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="children">For a child's key to its aggregate — Rask's own rule is <see cref="Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade" />.</param>
    /// <param name="others">For every other foreign key.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <see cref="Microsoft.EntityFrameworkCore.DeleteBehavior.ClientCascade" /> keeps the children going with their
    ///     aggregate while the database's own key says <c>NO ACTION</c> — what a schema another tool made usually has.
    ///     An <c>OnDelete</c> in an entity's own <c>Configure</c> wins.
    /// </remarks>
    public static ModelConfigurationBuilder DeleteBehavior(
        this ModelConfigurationBuilder configurationBuilder, DeleteBehavior children, DeleteBehavior others)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Add(_ => new DeleteBehaviorConvention(children, others));
        return configurationBuilder;
    }
}
