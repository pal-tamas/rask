using System.Text.Json.Serialization;

namespace Rask.Core.Components;

// Constraints for MediaCaptureTrigger, serialized into the gesture payload's `arg` and JSON.parse'd by the
// client's __raskMedia.getUserMedia (Web defaults → { "video": …, "audio": …, "facingMode": … }). Field
// order mirrors the public Rask.Wasm.Browser.MediaConstraints so the two can't be confused positionally.
internal sealed record GestureMediaConstraints(
    bool Video,
    bool Audio,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FacingMode);
