using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>A navigation on a read face, inferred from an id the write model holds.</summary>
/// <param name="navigation">The navigation's name — <c>ShippedByUser</c>.</param>
/// <param name="targetReadType">The read face it points at — <c>UserRead</c>.</param>
/// <param name="foreignKey">The id property it is inferred from — <c>ShippedByUserId</c>.</param>
public sealed class ReadReferenceMapping(
    string navigation,
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type targetReadType,
    string foreignKey)
{
    /// <summary>A navigation whose target's read face also holds the rows that point at it.</summary>
    /// <param name="navigation">The navigation's name — <c>FromFueling</c>.</param>
    /// <param name="targetReadType">The read face it points at — <c>FuelingRead</c>.</param>
    /// <param name="foreignKey">The id property it is inferred from — <c>FromFuelingId</c>.</param>
    /// <param name="inverse">The collection of these rows on the target's face — <c>DistributionsFrom</c>.</param>
    public ReadReferenceMapping(
        string navigation,
        [DynamicallyAccessedMembers(DataTrimming.Entity)] Type targetReadType,
        string foreignKey,
        string inverse)
        : this(navigation, targetReadType, foreignKey) =>
        Inverse = inverse;

    /// <summary>The navigation's name.</summary>
    public string Navigation { get; } = navigation;

    /// <summary>The read face it points at.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type TargetReadType { get; } = targetReadType;

    /// <summary>The id property it is inferred from.</summary>
    public string ForeignKey { get; } = foreignKey;

    /// <summary>The collection of these rows on the target's face, when it has one.</summary>
    internal string? Inverse { get; }
}
