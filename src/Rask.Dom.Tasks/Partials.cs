using System;
using System.Collections.Generic;

namespace Rask.Core.Dom.Build;

// What the hand-written partials beside the generated types declare.
internal sealed class Partials
{
    // Class -> the members it declares itself, which are therefore not generated.
    public Dictionary<string, HashSet<string>> Owned { get; } = new(StringComparer.Ordinal);

    // A typed control's MDN name -> its type parameter (HTMLInputElement -> T).
    public Dictionary<string, string> Typed { get; } = new(StringComparer.Ordinal);

    // Classes with a non-generic hand-written partial, which may implement WriteOwnedAttributes.
    public HashSet<string> HandWritten { get; } = new(StringComparer.Ordinal);
}
