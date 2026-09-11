namespace Rask.Data;

/// <summary>
///     Keeps a property off the form model the source generator writes for its entity — or, on the entity
///     itself, keeps the entity from getting one at all.
/// </summary>
/// <remarks>
///     <para>
///         Every mapped property of a <see cref="Model" /> is copied onto its generated <c>ProductModel</c>
///         by default, which is what a create or edit form wants. Mark the ones a form must never write — a
///         rating the application computes, a counter it maintains — and they are neither on the model nor
///         assigned by <c>Product.CreateAsync(model)</c> or <c>Product.UpdateAsync(id, model)</c>.
///     </para>
///     <para>
///         On the class, it is the way out for an entity that already has a hand-written
///         <c>ProductModel</c> beside it, or that is never edited through a form.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class SkipModelAttribute : Attribute;
