namespace Rask;

/// <summary>
/// The <c>data-ui-*</c> attribute a Flux-drawn component's root carries, where Flux writes <c>data-flux-*</c>.
/// </summary>
/// <remarks>
/// Written through the element's own <c>data-*</c> bag, so it lands in the slot the attribute order gives
/// <c>data-*</c>, ahead of whatever the call site added.
/// </remarks>
internal static class UiDataMarker
{
    /// <summary>The bag for one marker, built once: <c>Of("ui-heading")</c> writes <c>data-ui-heading</c>.</summary>
    internal static IReadOnlyDictionary<string, string?> Of(string name) =>
        new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = null };

    /// <summary>The marker, then the call site's own data.</summary>
    internal static IReadOnlyDictionary<string, string?> Join(
        IReadOnlyDictionary<string, string?> marker,
        IReadOnlyDictionary<string, string?>? data)
    {
        if (data is null)
        {
            return marker;
        }

        var joined = new Dictionary<string, string?>(marker, StringComparer.Ordinal);
        foreach (var (name, value) in data)
        {
            joined[name] = value;
        }

        return joined;
    }
}
