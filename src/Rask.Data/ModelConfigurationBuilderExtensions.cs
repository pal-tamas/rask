using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>The app-wide model rules an <see cref="IRaskModel" /> states in its <c>Conventions</c>.</summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    ///     Names every primary key, foreign key, unique constraint, index and (on SQL Server) default the model did not
    ///     name explicitly: <c>c.ConstraintNames(n =&gt; $"{n.Table}_{n.Joined}_idx")</c>.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="name">
    ///     The name for one constraint, or null to keep EF Core's. A name longer than the provider allows is cut to its
    ///     limit.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     Applied as the model is finalized, after every entity's own <c>Configure</c>: a <c>HasDatabaseName</c> or
    ///     <c>HasConstraintName</c> there wins.
    /// </remarks>
    public static ModelConfigurationBuilder ConstraintNames(
        this ModelConfigurationBuilder configurationBuilder, Func<Constraint, string?> name)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(name);

        configurationBuilder.Conventions.Add(services => new ConstraintNameConvention(
            name, string.Equals(services.GetService<IDatabaseProvider>()?.Name, ProviderNames.SqlServer, StringComparison.Ordinal)));
        return configurationBuilder;
    }

    /// <summary>
    ///     The order the database puts text in, for every text column: <c>c.TextOrder("Hungarian_100_CI_AS")</c> — so an
    ///     <c>OrderBy(x =&gt; x.Name)</c> puts <c>Cs</c> after <c>Cu</c> and <c>Ö</c> after <c>O</c>, as Hungarian does.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="collation">The database's name for the order — a SQL Server collation.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         On SQL Server every string column, the batteries' too, is declared with the collation, and a migration writes
    ///         it into the schema. A <c>UseCollation</c> in an entity's own <c>Configure</c> wins.
    ///     </para>
    ///     <para>
    ///         On every other database it changes nothing: the name is SQL Server's, and an app that runs on SQLite in
    ///         development keeps its schema as it was. Order a list already read with <c>TextOrder.In("hu")</c>.
    ///     </para>
    /// </remarks>
    public static ModelConfigurationBuilder TextOrder(this ModelConfigurationBuilder configurationBuilder, string collation)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collation);

        configurationBuilder.Conventions.Add(services => new TextOrderConvention(
            collation, string.Equals(services.GetService<IDatabaseProvider>()?.Name, ProviderNames.SqlServer, StringComparison.Ordinal)));
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
