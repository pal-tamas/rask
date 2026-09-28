using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>A screenshot shown in the install/app-store UI (<c>screenshots[]</c>).</summary>
/// <param name="Src">Image URL.</param>
/// <param name="Sizes">Space-separated sizes, e.g. <c>"1280x720"</c>.</param>
/// <param name="Type">MIME type, e.g. <c>"image/png"</c>.</param>
/// <param name="FormFactor">Target form factor: <c>"wide"</c> (desktop) or <c>"narrow"</c> (mobile).</param>
/// <param name="Label">Accessible label.</param>
public sealed record ManifestScreenshot(
    [property: JsonPropertyName("src")] string Src,
    [property: JsonPropertyName("sizes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sizes = null,
    [property: JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null,
    [property: JsonPropertyName("form_factor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FormFactor = null,
    [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);
