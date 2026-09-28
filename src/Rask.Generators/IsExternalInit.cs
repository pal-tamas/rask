// Polyfill for `init`-only setters / records on netstandard2.0.
// The Roslyn analyzer host runs on netstandard2.0; this type is defined so the
// compiler accepts `init` accessors when consuming records in the generator project.

namespace System.Runtime.CompilerServices;

#pragma warning disable S2094 // the compiler looks this polyfill up by name; it has no members by design
internal static class IsExternalInit
{
}
#pragma warning restore S2094
