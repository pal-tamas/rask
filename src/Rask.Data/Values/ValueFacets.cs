using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Rask.Data;

/// <summary>
///     What a value object declared from <see cref="Text" /> or <see cref="Number" /> tells its column, read from the
///     arguments it hands its base when the app is compiled — so the column is shaped without building an instance,
///     which would ask its rules. Written by generated code as <c>X.__Facets</c>; not an API to write against.
/// </summary>
/// <param name="MaxLength">The most characters (<c>Text(MaxLength: …)</c>), or null for no limit.</param>
/// <param name="Precision">The most digits in all (<c>Number(Precision: …)</c>), or null.</param>
/// <param name="Scale">The most decimal places (<c>Number(Scale: …)</c>), or null.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Auto)]
public readonly record struct ValueFacets(int? MaxLength = null, int? Precision = null, int? Scale = null);
