namespace Rask;

/// <summary>
///     The <c>data-ui-*</c> attribute a kit part writes on its root, where Flux writes <c>data-flux-*</c>.
/// </summary>
/// <remarks>
///     Written through the element's own <c>data-*</c> bag, so it lands in the slot the attribute order gives
///     <c>data-*</c>. A table writes one per cell, so the common case — a part whose call site set no
///     <c>data-*</c> of its own — hands back the same dictionary every time rather than building one per
///     element per render.
/// </remarks>
internal sealed class UiPartMarker(string name)
{
    private readonly Dictionary<string, string?> _alone = new(StringComparer.Ordinal) { [name] = null };

    /// <summary>The marker, then whatever the call site wrote; a call site's own entry of that name wins.</summary>
    internal IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? data) =>
        data is null ? _alone : Merge(new Dictionary<string, string?>(_alone, StringComparer.Ordinal), data);

    /// <summary>The marker, one more attribute of the part's own (<c>data-color</c>), then the call site's.</summary>
    internal IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? data, string key, string? value) =>
        Merge(new Dictionary<string, string?>(_alone, StringComparer.Ordinal) { [key] = value }, data);

    private static Dictionary<string, string?> Merge(Dictionary<string, string?> merged, IReadOnlyDictionary<string, string?>? data)
    {
        foreach (var (key, value) in data ?? merged)
        {
            merged[key] = value;
        }

        return merged;
    }
}
