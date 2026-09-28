using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>A home-screen shortcut / jump-list entry (<c>shortcuts[]</c>).</summary>
/// <param name="Name">Label shown in the shortcut menu.</param>
/// <param name="Url">URL opened by the shortcut (resolved against the page when applied).</param>
/// <param name="ShortName">Optional shorter label.</param>
/// <param name="Description">Optional accessible description.</param>
/// <param name="Icons">Optional icons for the shortcut.</param>
public sealed record ManifestShortcut(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("short_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShortName = null,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("icons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ManifestIcon>? Icons = null);
