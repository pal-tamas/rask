using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Tuning for a save file picker (adds a suggested name on top of <see cref="FilePickerOptions" />).</summary>
public sealed record SaveFilePickerOptions
{
    /// <summary>Pre-filled file name in the save dialog (the <c>suggestedName</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuggestedName { get; init; }

    /// <inheritdoc cref="FilePickerOptions.Description" />
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <inheritdoc cref="FilePickerOptions.Accept" />
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Accept { get; init; }
}
