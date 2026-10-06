namespace Rask;

/// <summary>
///     The <c>data-ui-*</c> attribute a kit part writes on its root, where Flux writes <c>data-flux-*</c>.
/// </summary>
/// <remarks>
///     A table writes one per cell, so the common case — a part whose call site set no <c>data-*</c> of its
///     own — hands back the same dictionary every time rather than building one per element per render.
/// </remarks>
internal sealed class UiPartMarker(string name)
{
    private readonly Dictionary<string, string?> _alone = new(StringComparer.Ordinal) { [name] = "" };

    /// <summary>The marker, then whatever the call site wrote; a call site's own entry of that name wins.</summary>
    internal IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? data)
    {
        if (data is null)
        {
            return _alone;
        }

        var merged = new Dictionary<string, string?>(_alone, StringComparer.Ordinal);
        foreach (var (key, value) in data)
        {
            merged[key] = value;
        }

        return merged;
    }
}
