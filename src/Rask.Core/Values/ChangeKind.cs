namespace Rask.Values;

/// <summary>What a <see cref="Change{T}" /> does to the value it is about.</summary>
public enum ChangeKind
{
    /// <summary>Leaves it as it is.</summary>
    Keep,

    /// <summary>Takes it away.</summary>
    Clear,

    /// <summary>Sets it to a new value.</summary>
    Set,
}
