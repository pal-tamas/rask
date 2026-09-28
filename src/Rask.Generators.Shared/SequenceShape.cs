namespace Rask.Generators.Shared;

/// <summary>How a sequence is rebuilt after its elements are read.</summary>
internal enum SequenceShape
{
    /// <summary>A <c>T[]</c>.</summary>
    Array,

    /// <summary>A concrete <c>List&lt;T&gt;</c>.</summary>
    List,

    /// <summary>An interface a <c>List&lt;T&gt;</c> satisfies.</summary>
    Interface,
}
