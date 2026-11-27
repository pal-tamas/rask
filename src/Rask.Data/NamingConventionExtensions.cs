using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     How the app's tables and columns are named, when not as Rask names them (<c>delivery_notes</c>, <c>pistol_id</c>):
///     stated once in its <see cref="IRaskModel" />'s <c>Conventions</c>.
/// </summary>
public static class NamingConventionExtensions
{
    /// <summary>
    ///     Names every column the model did not name explicitly, in place of Rask's snake case (<c>n.SnakeCase</c>):
    ///     <c>c.ColumnNames(n =&gt; n.CamelCase)</c> maps <c>CountedAt</c> to <c>countedAt</c>, and <c>Start</c> inside a
    ///     <c>Km</c> value object to <c>kmStart</c>.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="name">The column's name. Empty keeps EF Core's, as <c>n.AsDeclared</c> does.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Applied as the model is finalized, before anything that reads a column's name (a constraint's name, a
    ///         unique index's filter): a <c>HasColumnName</c> or <c>[Column]</c> in an entity's own <c>Configure</c>
    ///         wins.
    ///     </para>
    ///     <para>
    ///         The tables of Rask's own batteries (jobs, mail, auth, …) are named too. A JSON column is named, the
    ///         properties inside it are not, and an owned type's key that is its owner's key stays the owner's column.
    ///     </para>
    /// </remarks>
    public static ModelConfigurationBuilder ColumnNames(this ModelConfigurationBuilder configurationBuilder, Func<ColumnName, string> name)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(name);

        StoreNameConvention.For(configurationBuilder).Columns = name;
        return configurationBuilder;
    }

    /// <summary>
    ///     Names every table the model did not name explicitly, in place of Rask's plural snake case
    ///     (<c>t.SnakeCasePlural</c>): <c>c.TableNames(t =&gt; t.Class)</c> maps <c>Fueling</c> to <c>Fueling</c>,
    ///     whatever its <c>DbSet</c> is called.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    /// <param name="name">The table's name. Empty keeps EF Core's, as <c>t.AsDeclared</c> does.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Applied as the model is finalized, before anything that reads a table's name: a <c>ToTable</c> or
    ///         <c>[Table]</c> in an entity's own <c>Configure</c> wins, and the schema is left as it is.
    ///     </para>
    ///     <para>
    ///         One table per hierarchy is named once, after its root, and an owned type in a table of its own after its
    ///         class. The tables of Rask's own batteries are named too; a view keeps the one <c>ToView</c> gave it.
    ///     </para>
    /// </remarks>
    public static ModelConfigurationBuilder TableNames(this ModelConfigurationBuilder configurationBuilder, Func<TableName, string> name)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(name);

        StoreNameConvention.For(configurationBuilder).Tables = name;
        return configurationBuilder;
    }
}
