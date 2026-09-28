namespace Rask.Generators.Shared;

/// <summary>A navigation on a read face, once its target is known.</summary>
/// <param name="Name">The navigation's name, taken from the PROPERTY — <c>ShippedByUser</c> from <c>ShippedByUserId</c>.</param>
/// <param name="TargetReadType">The read face it points at — <c>global::Shop.UserRead</c>.</param>
/// <param name="ForeignKey">The id property it is inferred from — <c>ShippedByUserId</c>.</param>
/// <param name="Nullable">Whether the id is nullable, and so the navigation too.</param>
internal sealed record ReadNavigation(
    string Name,
    string TargetReadType,
    string ForeignKey,
    bool Nullable);
