using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Puts the writes on the model type itself, so <c>Product.Create(entity)</c> needs no
///     <see cref="DbContext" /> in scope to reach it through.
/// </summary>
/// <remarks>
///     <para>
///         These are C# 14 static extension members over every type deriving from <see cref="Aggregate{TId}" />,
///         which is why nothing has to be declared or derived from a second base: an entity that compiles
///         today has them.
///     </para>
///     <para>
///         <b>An aggregate is not a query surface.</b> <c>Product.Where(…)</c>, <c>Product.All</c>,
///         <c>Product.FindAsync(id)</c> and the terminals that went with them are gone: querying works
///         through the generated read face, <c>Product.Read</c>, which is made of primitives and carries the
///         navigations an aggregate is not allowed to have. An aggregate holds another's id and nothing more,
///         so the two sides cannot be the same surface — a query that reached across a border on the write
///         side would be the border failing.
///     </para>
///     <para>
///         The three doors that replace it, each for a different need:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Show one, or many, or joined</b> — <c>Product.Read.Where(p =&gt; p.Active)</c>. Untracked,
///                 opens its own context, and by-id is just the narrowest case:
///                 <c>Product.Read.Where(p =&gt; p.Id == id).FirstOrDefaultAsync()</c>.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Fill a form</b> — <c>Product.Model(id)</c>, which is the edit shape the generated
///                 model declares, nested value objects and children included.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Load it to change it</b> — <c>Product.Update(id, p =&gt; p.Rename(name))</c>, or a
///                 context when the decision needs the row in hand first.
///             </description>
///         </item>
///     </list>
///     <para>
///         <b>Writes stay on the type.</b> <c>Product.Create(entity)</c> is here; the source generator
///         adds the form-model writes beside the entity — <c>Product.Create(ProductModel)</c>,
///         <c>Product.Update(id, ProductModel)</c>, <c>Product.Delete(id)</c>. Each goes through the
///         change tracker, so the interceptors always run, and each takes an optional context to join. Plain
///         EF Core through an injected context stays available for anything richer.
///     </para>
///     <para>
///         A member declared on the entity itself always wins over one of these, so an entity with its own
///         static <c>Create</c> keeps it.
///     </para>
/// </remarks>
public static class ModelSet
{
    extension<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(TEntity)
        where TEntity : class, IAggregate
    {
        /// <summary>Inserts an entity the caller built — through its own factory and methods — and saves.</summary>
        /// <remarks>
        ///     The domain-operation form of create: the entity's constructor keeps its invariants, and Rask only
        ///     persists it. A form's values go through the generated <c>Product.Create(ProductModel)</c>
        ///     instead.
        /// </remarks>
        /// <param name="entity">The entity to insert.</param>
        /// <param name="db">
        ///     The context to insert through, or <c>null</c> to open one. A given context is saved — with anything
        ///     else pending on it — and is not disposed, so the insert joins the caller's transaction.
        /// </param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>The inserted entity, with any store-generated key filled in.</returns>
        public static Task<TEntity> Create(
            TEntity entity,
            DbContext? db = null,
            CancellationToken cancellationToken = default) =>
            GeneratedModelWrites.Create(entity, db, cancellationToken);
    }
}
