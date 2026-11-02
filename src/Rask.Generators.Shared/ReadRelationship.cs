using Rask.Batteries.Generators;

namespace Rask.Generators.Shared;

/// <summary>
///     A relationship an entity's own <c>Configure</c> declares —
///     <c>b.HasOne&lt;Provider&gt;().WithMany().HasForeignKey(c =&gt; c.TransactionTypeId)</c> — which the read face
///     follows instead of guessing from the id's name.
/// </summary>
/// <param name="DependentTypeName">The type holding the foreign key — <c>global::Eszaf.ProviderCountry</c>.</param>
/// <param name="PrincipalTypeName">The type it points at — <c>global::Eszaf.Provider</c>.</param>
/// <param name="ForeignKeys">The foreign key's members, in order: one, or several for a composite key.</param>
/// <param name="OnAlternateKey">
///     Whether it joins on a <c>HasPrincipalKey</c> rather than the principal's key, which a read navigation cannot
///     carry, so the id gets none — and no guess either.
/// </param>
/// <param name="PrincipalCollection">
///     The principal's collection the chain names — <c>HasMany(c =&gt; c.Countries)</c> or <c>WithMany(c =&gt; c.Countries)</c>
///     — or null. When it is the collection a root holds its children in, the relationship is the child's way back to
///     that root, which the read face maps already.
/// </param>
internal sealed record ReadRelationship(
    string DependentTypeName,
    string PrincipalTypeName,
    EquatableArray<string> ForeignKeys,
    bool OnAlternateKey,
    string? PrincipalCollection);
