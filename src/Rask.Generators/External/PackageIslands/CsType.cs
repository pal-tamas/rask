using System;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A C# type a snapshot type maps to, as a tree the emitter walks.</summary>
/// <param name="Kind"><c>string</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>enum</c>, <c>union</c>, <c>record</c>, <c>list</c> or <c>map</c>.</param>
/// <param name="Fqn">The non-nullable type, fully qualified.</param>
/// <param name="IsValueType">Whether <c>?</c> makes it a <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="Element">A list's element or a map's value.</param>
/// <param name="ElementNullable">Whether that element may be null.</param>
internal sealed record CsType(string Kind, string Fqn, bool IsValueType, CsType? Element, bool ElementNullable)
{
    /// <summary>The type spelled as a nullable one.</summary>
    public string NullableFqn => Fqn + "?";
}
