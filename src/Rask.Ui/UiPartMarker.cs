namespace Rask;

/// <summary>
///     The <c>data-ui-*</c> attribute a kit part writes on its root, where Flux writes <c>data-flux-*</c>.
/// </summary>
/// <remarks>
///     Written through the element's own <c>data-*</c> bag, so it lands in the slot the attribute order gives
///     <c>data-*</c>. Bare by default; a slot carries a value (<c>data-slot="text"</c>). A table writes one per cell, so the common case — a part whose call site set no
///     <c>data-*</c> of its own — hands back the same dictionary every time rather than building one per
///     element per render.
/// </remarks>
internal sealed class UiPartMarker
{
    private readonly Dictionary<string, string?> _alone;

    internal UiPartMarker(string name, string? value = null) =>
        _alone = new(StringComparer.Ordinal) { [name] = value };

    private UiPartMarker(Dictionary<string, string?> marks) => _alone = marks;

    /// <summary>This marker and one more beside it, for a root Flux marks twice (<c>data-flux-button data-flux-group-target</c>).</summary>
    internal UiPartMarker And(string name, string? value = null) =>
        new(new Dictionary<string, string?>(_alone, StringComparer.Ordinal) { [name] = value });

    /// <summary>The marker, then whatever the call site wrote; a call site's own entry of that name wins.</summary>
    internal IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? data) =>
        data is null ? _alone : Merge(new Dictionary<string, string?>(_alone, StringComparer.Ordinal), data);

    /// <summary>The marker, one more attribute of the part's own (<c>data-color</c>), then the call site's.</summary>
    internal IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? data, string key, string? extra) =>
        Merge(new Dictionary<string, string?>(_alone, StringComparer.Ordinal) { [key] = extra }, data);

    private static Dictionary<string, string?> Merge(Dictionary<string, string?> merged, IReadOnlyDictionary<string, string?>? data)
    {
        foreach (var (key, value) in data ?? merged)
        {
            merged[key] = value;
        }

        return merged;
    }
}
