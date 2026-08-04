using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// netstandard2.0 has no IsExternalInit, which the compiler requires for `init` accessors (and therefore
// for records). Declaring it here lets the generator projects use records in their models.
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
