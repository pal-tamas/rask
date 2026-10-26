using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The rules for the whole model, in one class the source generator finds: a string length, a decimal's
///     precision, how constraints are named, what a delete does.
/// </summary>
/// <remarks>
///     <para>
///         One per app. Both members are optional, and both reach every table — the app's entities and the
///         batteries' — through <see cref="RaskDbContext" />, <c>RaskAppDbContext</c> and any context of the
///         app's own deriving from either. A second one in the same project is RASK110.
///     </para>
///     <para>
///         An entity's own static <c>Configure</c> still runs after <see cref="Model" />, and a name or a delete
///         behaviour it states explicitly wins over the app-wide rule.
///     </para>
///     <example>
///         <code>
///         public sealed class AppModel : IRaskModel
///         {
///             public static void Conventions(ModelConfigurationBuilder c)
///             {
///                 c.Properties&lt;string&gt;().HaveMaxLength(1000);
///                 c.ConstraintNames(n =&gt; n.Kind switch
///                 {
///                     ConstraintKind.PrimaryKey =&gt; $"{n.Table}_pkey",
///                     _ =&gt; null,
///                 });
///                 c.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);
///             }
///
///             public static void Model(ModelBuilder m) =&gt; m.HasDefaultSchema("dbo");
///         }
///         </code>
///     </example>
/// </remarks>
public interface IRaskModel
{
    /// <summary>
    ///     The conventions every property and relationship is mapped under — EF Core's
    ///     <c>ConfigureConventions</c>, for the whole app.
    /// </summary>
    /// <param name="configurationBuilder">The conventions being configured.</param>
    static virtual void Conventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    /// <summary>
    ///     What the model as a whole carries — a default schema, a sequence. Runs after Rask's conventions and
    ///     before each entity's own <c>Configure</c>.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    static virtual void Model(ModelBuilder modelBuilder)
    {
    }
}
