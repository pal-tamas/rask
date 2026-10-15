using System.Text.Json;

namespace Rask;

/// <summary>
///     What a <see cref="UiColorPicker" /> tells the runtime that keeps it in the browser (<c>rask-color.ts</c>):
///     which part each element is, and the words it writes there.
/// </summary>
internal static class UiColorPickerMarks
{
    /// <summary>The attribute that names a part.</summary>
    internal const string Part = "data-rask-color-part";

    /// <summary>A bound field whose value waits for the reader's next action, and goes ahead of it.</summary>
    internal static readonly (string, string?)[] WaitsForAction = [("data-rask-bind-on", "action")];

    /// <summary>The live region the runtime says a picked swatch in.</summary>
    internal static readonly Dictionary<string, string?> Announced = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-announce"] = string.Empty,
        ["rask-color-part"] = "announce",
    };

    /// <summary>The pair that names a part, for <c>Attributes(…)</c>.</summary>
    internal static (string, string?) Named(string part) => (Part, part);

    /// <summary>The format as the runtime writes it: <c>hex</c>, <c>hexa</c>, <c>rgb</c>, <c>rgba</c>, <c>hsl</c> or <c>hsla</c>.</summary>
    internal static string FormatName(Ui.ColorPickerFormat format) => format switch
    {
        Ui.ColorPickerFormat.Hexa => "hexa",
        Ui.ColorPickerFormat.Rgb => "rgb",
        Ui.ColorPickerFormat.Rgba => "rgba",
        Ui.ColorPickerFormat.Hsl => "hsl",
        Ui.ColorPickerFormat.Hsla => "hsla",
        _ => "hex",
    };

    /// <summary>The words the runtime writes, in the reader's language, as one JSON object.</summary>
    internal static string Says() =>
        "{" + Word("open", RaskStrings.Get(RaskString.ColorPickerOpen, "Open color picker"))
        + "," + Word("openWith", RaskStrings.Get(RaskString.ColorPickerOpenWith, "Open color picker, {0}", "{0}"))
        + "," + Word("selected", RaskStrings.Get(RaskString.ColorPickerSelected, "{0} selected", "{0}"))
        + "," + Word("area", RaskStrings.Get(RaskString.ColorPickerAreaValue, "Saturation {0}%, brightness {1}%", "{0}", "{1}"))
        + "}";

    private static string Word(string name, string text) =>
        "\"" + name + "\":\"" + JsonEncodedText.Encode(text) + "\"";
}
