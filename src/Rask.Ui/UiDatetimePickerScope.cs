namespace Rask;

/// <summary>What a datetime picker tells the button that opens it — its own, or a <see cref="UiDatetimePickerButton" /> in its <c>Trigger</c> slot.</summary>
internal sealed class UiDatetimePickerScope
{
    internal required string PopoverId { get; init; }

    internal required string ControlId { get; init; }

    internal required string Placeholder { get; init; }

    /// <summary>What the button shows — a moment's date, word and time, or a range's two ends — and nothing while unset.</summary>
    internal (string Before, string Between, string After)? Text { get; init; }

    internal bool Disabled { get; init; }

    internal bool Invalid { get; init; }

    internal IReadOnlyDictionary<string, string?> Aria { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Whether the picker asked for the button that empties it.</summary>
    internal bool Clearable { get; init; }

    /// <summary>Empties the picker: nothing chosen.</summary>
    internal required Func<Task> Clear { get; init; }
}
