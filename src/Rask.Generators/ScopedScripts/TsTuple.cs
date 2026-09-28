using System.Collections.Generic;

namespace Rask.Generators.ScopedScripts;

internal sealed class TsTuple(List<TsTupleElement> elements) : ITsType
{
    public List<TsTupleElement> Elements { get; } = elements;
}
