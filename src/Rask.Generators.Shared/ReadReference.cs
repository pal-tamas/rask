namespace Rask.Generators.Shared;

/// <summary>
///     An id that MIGHT be a reference to another aggregate — resolved once every entity is known.
/// </summary>
/// <remarks>
/// Inference cannot happen while walking one entity: whether <c>CustomerId</c> points anywhere depends on
/// what else the compilation declares. So the symbol pass records the candidate and the emit pass resolves
/// it, which is also what keeps symbols out of an incremental generator's pipeline.
/// </remarks>
/// <param name="Property">The id property — <c>ShippedByUserId</c>.</param>
/// <param name="Target">The property's name without <c>Id</c> — <c>ShippedByUser</c>, which the navigation takes.</param>
/// <param name="IdTypeName">The id's type, for checking it against the target's key.</param>
/// <param name="Nullable">Whether the id is nullable, and so the navigation too.</param>
internal sealed record ReadReference(
    string Property,
    string Target,
    string IdTypeName,
    bool Nullable);
