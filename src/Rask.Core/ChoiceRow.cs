using System.Runtime.InteropServices;

namespace Rask;

/// <summary>One row a picker offers: the id it stands for, the words it shows, and a muted line under them.</summary>
/// <typeparam name="TKey">The id's type.</typeparam>
/// <param name="Id">The id the row stands for — what the bound member holds once it is picked.</param>
/// <param name="Text">The words the row shows, and what a search finds it by.</param>
/// <param name="Detail">The muted line under the words, which a search finds it by too; null for none.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ChoiceRow<TKey>(TKey Id, string Text, string? Detail = null);
