// Polyfill for `init`-only setters / records on netstandard2.0.
// The Roslyn analyzer host runs on netstandard2.0; this type is defined so the
// compiler accepts `init` accessors when consuming records in the generator project.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
