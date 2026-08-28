namespace Rask.Core;

/// <summary>
///     Machinery. Lets the children indexer turn a chain back into a component without knowing which
///     component the chain builds.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Build{T}" /> already converts to the component it builds implicitly, which is what
///         makes a chain usable as a child. That conversion is user-defined, and a user-defined conversion
///         never lifts through <c>IEnumerable&lt;&gt;</c> — so a projection of chains cannot reach
///         <c>IEnumerable&lt;Component?&gt;</c>. An interface would normally close that gap by variance,
///         but <see cref="Build{T}" /> is a struct (the chain must not allocate) and variance does not
///         apply to value types.
///     </para>
///     <para>
///         So the unwrap happens one element at a time, from
///         <see cref="Component.this[object?[]]" />, through this interface. Implemented explicitly on
///         both chain structs so it never appears in completion beside the chain steps.
///     </para>
/// </remarks>
public interface IComponentChain
{
    /// <summary>The component this chain has built.</summary>
    Component Unwrap();
}
