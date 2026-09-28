using System.Text.Json.Serialization;

namespace Rask.Core.Components;

// Shared payload for the data-rask-gesture attribute: the capability, plus — when a result is expected —
// the id the client posts the result back under (RaskGestureResult); an optional per-capability argument
// (an orientation type, JSON media constraints); and an optional target element ref-id (the <video> for
// picture-in-picture / media capture). Serialized with the trim-safe source-gen context (Web defaults →
// { "cap": …, "rid": … }); arg/el are omitted when null so the common two-field form stays compact.
internal sealed record GesturePayload(
    string Cap,
    int? Rid,
    [property: JsonPropertyName("arg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Arg = null,
    [property: JsonPropertyName("el"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? El = null);
