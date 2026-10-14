using System.Globalization;
using Rask.Core;

namespace Rask;

// The popover: a modal <dialog> holding the area, the sliders, the text input and the palette.
public sealed partial class UiColorPicker
{
    private const string Hues = "background:linear-gradient(to right, #ff0000, #ffff00, #00ff00, #00ffff, #0000ff, #ff00ff, #ff0000)";

    private const string Checkerboard =
        "background-image:linear-gradient(45deg, #ccc 25%, transparent 25%), linear-gradient(-45deg, #ccc 25%, transparent 25%), "
        + "linear-gradient(45deg, transparent 75%, #ccc 75%), linear-gradient(-45deg, transparent 75%, #ccc 75%);"
        + "background-size:8px 8px;background-position:0 0, 0 4px, 4px -4px, -4px 0";

    // The keys the area and a swatch handle themselves, which must not also scroll the page or press a button.
    private const string AreaKeys = "Arrows Home End PageUp PageDown";

    private const string SwatchKeys = "Arrows Home End PageUp PageDown Space Enter";

    // Escape and a press outside close it, and `data-open` says it shows: the runtime's (rask-overlay.ts).
    private static readonly Dictionary<string, string?> DialogMarks = new(StringComparer.Ordinal) { ["rask-modal"] = "any" };

    private static readonly Dictionary<string, string?> AreaMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-area"] = string.Empty,
        ["rask-drag"] = "x y",
        ["rask-press-keeps-focus"] = string.Empty,
        ["rask-color-part"] = "area",
    };

    private static readonly Dictionary<string, string?> AreaOff = new(StringComparer.Ordinal) { ["ui-color-picker-area"] = string.Empty };

    private static readonly Dictionary<string, string?> ThumbMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-area-thumb"] = string.Empty,
        ["ui-color-picker-initial-focus"] = string.Empty,
        ["rask-contain-keys"] = AreaKeys,
    };

    private static readonly Dictionary<string, string?> SwatchesMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-swatches"] = string.Empty,
        ["rask-contain-keys"] = SwatchKeys,
        ["rask-color-part"] = "swatches",
    };

    private static readonly Dictionary<string, string?> ProbeMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-sheet"] = string.Empty,
        ["rask-color-part"] = "sheet",
    };

    private IReadOnlyList<UiColorSwatch>? _paletteOf;

    private UiColorPalette? _ownPalette;

    private string? _labelled;

    private int _renders;

    private UiColorPalette Palette
    {
        get
        {
            if (Swatches is not { } own)
            {
                return UiColorPalette.Standard();
            }

            if (_ownPalette is null || !ReferenceEquals(_paletteOf, own))
            {
                (_paletteOf, _ownPalette) = (own, UiColorPalette.From(own));
            }

            return _ownPalette;
        }
    }

    private Component Popover(string? labelled)
    {
        _labelled = labelled;
        // A new scope every render: the parts a page composed read it, and are drawn again when it changes.
        var scope = new UiColorPickerScope(this, ++_renders);
        var layout = Children?.Any() == true ? Children : [DefaultLayout()];

        // The browser opens it (the trigger's command, ArrowDown in the trigger) and closes it (Escape, a press
        // outside, the sheet's buttons, a swatch picked on the sheet), and nothing of that is the page's.
        return Dialog.Id(DialogId).Class(UiColorPickerLook.Dialog).Data(DialogMarks)[
            Div.Class(UiColorPickerLook.FocusPlaceholder).Data("ui-focus-placeholder", string.Empty).TabIndex(0),
            Context.Provide(scope)[layout],
            // Flux asks the window how wide it is; the kit asks the stylesheet, through a box only it sizes:
            // two pixels wide on the sheet. The browser reads it as a choice is made.
            Span.Class(UiColorPickerLook.SheetProbe).Data(ProbeMarks).Aria("hidden", "true")
        ];
    }

    private Component DefaultLayout()
    {
        var palette = Palette;

        return Div.Class(UiColorPickerLook.Layout)[
            DrawArea(null),
            DrawSlider(Ui.ColorPickerChannel.Hue),
            HasAlpha ? DrawSlider(Ui.ColorPickerChannel.Alpha) : null,
            Div.Class(UiColorPickerLook.InputRow)[
                DrawInput(null, UiColorPickerLook.PopoverInputInRow),
                Dropper == true ? DrawDropper(null) : null
            ],
            palette.Swatches.Count > 0 ? DrawSwatches(palette, null) : null,
            // Cancel and Select are the sheet's. Each closes the dialog by command; Select's press is heard on
            // the box around it, where Flux hears it too, and the colour the sheet shows is taken.
            Div.Class(UiColorPickerLook.Footer)[
                Div.Class("inline")[Footer(Ui.Button.Ghost, RaskStrings.Get(RaskString.ColorPickerCancel, "Cancel"))],
                Div.Class("inline").Role("button").TabIndex(0).Attributes(UiColorPickerMarks.Named("select"))[
                    Footer(Ui.Button.Primary, RaskStrings.Get(RaskString.ColorPickerSelect, "Select"))]
            ]
        ];
    }

    // Under a picker that is off the dialog never opens; its buttons are drawn as Flux draws them there, which
    // is not how a disabled button of the kit looks.
    private Component Footer(UiButton button, string text) =>
        button.Command("close").CommandFor(DialogId).Class(Off ? UiColorPickerLook.FooterOff : null)[text];

    /// <summary>Flux's <c>flux:color-picker.area</c>: saturation across, brightness up.</summary>
    internal Component DrawArea(string? @class)
    {
        var color = _kept.Color;
        var (across, down) = (Fraction(color.Saturation / 100), Fraction(1 - (color.Brightness / 100)));

        return Div.Class(UiClass.Compose(UiColorPickerLook.Area, @class)).Data(Off ? AreaOff : AreaMarks)[
            Div.Class(UiColorPickerLook.AreaLayer).Style("background-color:" + color.HueCss)
                .Attributes(("data-ui-color-picker-area-hue", null), UiColorPickerMarks.Named("hue-layer")),
            Div.Class(UiColorPickerLook.AreaWhite).Data("ui-color-picker-area-gradient", string.Empty),
            Div.Class(UiColorPickerLook.AreaBlack),
            // The thumb stands where the runtime last put it, and where the colour says until it has.
            Div.Class(UiColorPickerLook.AreaThumb)
                .Style("inset-inline-start:calc(var(--rask-drag-x, " + across + ") * 100%);top:calc(var(--rask-drag-y, " + down + ") * 100%)")
                .Data(ThumbMarks)
                .Role("slider")
                .TabIndex(0)
                // The dialog focuses it as it opens: Flux's initial focus.
                .Autofocus()
                .Aria(AreaAria(color)),
            Input.Value(across + " " + down).Type(InputType.Hidden)
        ];
    }

    private static Dictionary<string, string?> AreaAria(UiColorValue color)
    {
        var saturation = Whole(color.Saturation);
        var name = RaskStrings.Get(RaskString.ColorPickerArea, "Saturation and brightness");
        var kind = RaskStrings.Get(RaskString.ColorPickerAreaRole, "2D color picker");
        var said = RaskStrings.Get(RaskString.ColorPickerAreaValue, "Saturation {0}%, brightness {1}%", saturation, Whole(color.Brightness));

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = name,
            ["roledescription"] = kind,
            ["valuemin"] = "0",
            ["valuemax"] = "100",
            ["valuenow"] = saturation,
            ["valuetext"] = said,
        };
    }

    /// <summary>Flux's <c>flux:color-picker.slider</c>: the hue, or the opacity.</summary>
    internal Component DrawSlider(Ui.ColorPickerChannel channel)
    {
        var alpha = channel == Ui.ColorPickerChannel.Alpha;
        var max = Max(channel);
        var value = ChannelValue(channel);
        var rangeId = Prefixed + (alpha ? "-alpha" : "-hue");
        var along = Fraction((double)value / max);
        // The thumb's centre travels between the track's rounded ends, half a thumb in from each edge.
        var at = "calc(var(--ui-slider-thumb) / 2 + (100% - var(--ui-slider-thumb)) * var(--rask-drag-x, " + along + "))";
        var name = alpha ? RaskStrings.Get(RaskString.ColorPickerOpacity, "Opacity") : RaskStrings.Get(RaskString.ColorPickerHue, "Hue");
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { { "label", name } };
        if (_labelled is not null)
        {
            aria["labelledby"] = _labelled;
        }

        return Div.Class(UiColorPickerLook.Slider).Data(SliderMarks(alpha, rangeId)).Role("presentation").TabIndex(-1)[
            Div.Class(UiColorPickerLook.SliderRow).Data("ui-slider-track", string.Empty)[
                Div.Class(UiColorPickerLook.SliderTrack).Data("ui-slider-track", string.Empty)[
                    alpha
                        ? Div.Class(UiColorPickerLook.SliderPaint).Style(Checkerboard).Data("ui-color-picker-slider-checkerboard", string.Empty)
                        : Div.Class(UiColorPickerLook.SliderPaint).Style(Hues),
                    alpha
                        ? Div.Class(UiColorPickerLook.SliderPaint)
                            .Style("background:linear-gradient(to right, transparent, " + _kept.Color.OpaqueCss + ")")
                            .Attributes(("data-ui-color-picker-slider-track", null), UiColorPickerMarks.Named("fade"))
                        : null,
                    Div.Class(UiColorPickerLook.SliderFill)[
                        Div.Class(UiColorPickerLook.SliderIndicator).Style("inset-inline:0 calc(100% - " + at + ")").Data("ui-slider-indicator", string.Empty)
                    ],
                    Div.Class(UiColorPickerLook.SliderThumb).Style("inset-inline-start:" + at).Data("ui-slider-thumb", string.Empty)[
                        // The browser's: its arrows are kept from it and stepped by the runtime, its other
                        // keys are its own.
                        Input
                            .Value(value.ToString(CultureInfo.InvariantCulture))
                            .Type(InputType.Range)
                            .Id(rangeId)
                            .Min("0")
                            .Max(max.ToString(CultureInfo.InvariantCulture))
                            .Step("1")
                            .Disabled(Off)
                            .Class("sr-only")
                            .Aria(aria)
                            .Data("rask-contain-keys", "Arrows")
                    ]
                ]
            ],
            Off ? null : Input.Value(along).Type(InputType.Hidden)
        ];
    }

    // The track is the runtime's drag surface, half a thumb shorter at each end, and a press on it puts focus on
    // the thumb's own input, as Flux's does.
    private Dictionary<string, string?> SliderMarks(bool alpha, string rangeId)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-color-picker-slider"] = string.Empty,
            ["channel"] = alpha ? "alpha" : "hue",
        };
        if (!Off)
        {
            marks["rask-drag"] = "x";
            // Half the thumb, which is larger on the sheet: the stylesheet's, read by the runtime as it drags.
            marks["rask-drag-inset"] = "8";
            marks["rask-focus"] = rangeId;
            marks["rask-color-part"] = alpha ? "alpha" : "hue";
        }

        return marks;
    }

    /// <summary>Flux's <c>flux:color-picker.input</c>: the popover's own text input.</summary>
    /// <param name="placeholder">Shown while there is no colour, in place of the picker's own.</param>
    /// <param name="class">Classes added to the input's own.</param>
    internal Component DrawInput(string? placeholder, string? @class = null) =>
        Input
            .Value(_kept.Text(Written))
            .Type(InputType.Text)
            .Placeholder(placeholder ?? Hint)
            .Disabled(Off)
            .Class(UiClass.Compose(UiColorPickerLook.PopoverInput, @class))
            .Aria("label", RaskStrings.Get(RaskString.ColorPickerValue, "Color value"))
            .Attributes(("data-ui-color-picker-popover-input", null), UiColorPickerMarks.Named("popover-text"));

    /// <summary>Flux's <c>flux:color-picker.dropper</c>: picks a colour from the screen, in the click's own gesture.</summary>
    /// <remarks>
    ///     Rendered hidden and shown by the runtime where the browser has an EyeDropper: Flux's is not there
    ///     where the API is missing.
    /// </remarks>
    internal Component DrawDropper(string? @class) =>
        Trigger.EyeDropper
            .Template(gesture => Button
                .Type(ButtonType.Button)
                .Class(UiClass.Compose(UiColorPickerLook.Dropper, @class))
                .Disabled(Off)
                .Hidden()
                .Aria("label", RaskStrings.Get(RaskString.ColorPickerDropper, "Pick a color from the screen"))
                .Attributes(new Dictionary<string, string?>(gesture, StringComparer.Ordinal)
                {
                    ["data-ui-color-picker-dropper"] = null,
                    ["data-rask-requires"] = "EyeDropper",
                })[
                Ui.Icon.Name(Ui.IconName.EyeDropper).Mini
            ])
            .OnColor(DroppedAsync);

    /// <summary>Flux's <c>flux:color-picker.swatches</c>: a listbox of colours with one tab stop.</summary>
    /// <param name="own">The swatches written as the part's children, or <c>null</c> for the picker's palette.</param>
    /// <param name="class">Classes added to the container's own.</param>
    internal Component DrawSwatches(IReadOnlyList<UiColorSwatch>? own, string? @class) =>
        DrawSwatches(own is null ? Palette : UiColorPalette.From(own), @class);

    private Component DrawSwatches(UiColorPalette palette, string? @class)
    {
        var picked = _kept.Empty ? -1 : palette.IndexOf(Matched(_kept));
        var stop = Math.Max(picked, 0);

        return Div.Class(UiClass.Compose(UiColorPickerLook.Swatches, @class))
            .Data(SwatchesMarks)
            .Role("listbox")
            .Aria("label", RaskStrings.Get(RaskString.ColorPickerSwatches, "Color swatches"))[
            palette.Swatches.Select((swatch, index) => DrawSwatch(palette, index, index == picked, index == stop))
        ];
    }

    private HTMLButtonElement DrawSwatch(UiColorPalette palette, int index, bool picked, bool stop)
    {
        var swatch = palette.Swatches[index];
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-color-picker-swatch"] = string.Empty,
            ["value"] = swatch.Value,
        };
        if (picked)
        {
            marks["selected"] = string.Empty;
        }

        return Button
            .Key(index)
            .Type(ButtonType.Button)
            .Class(UiColorPickerLook.Swatch)
            .Style("background-color:" + swatch.Value)
            .Disabled(Off)
            .Data(marks)
            .Role("option")
            .TabIndex(stop ? 0 : -1)
            .Aria(SwatchAria(picked, swatch.Named));
    }

    private static Dictionary<string, string?> SwatchAria(bool picked, string label) => new(StringComparer.Ordinal)
    {
        ["selected"] = picked ? "true" : "false",
        ["label"] = label,
    };

    private int ChannelValue(Ui.ColorPickerChannel channel)
    {
        var value = channel == Ui.ColorPickerChannel.Alpha ? _kept.Color.Alpha * 100 : _kept.Color.Hue;

        return double.IsNaN(value) ? 0 : (int)Math.Floor(value + 0.5);
    }

    private static int Max(Ui.ColorPickerChannel channel) => channel == Ui.ColorPickerChannel.Alpha ? 100 : 360;

    // The swatch a colour is told from, by what the picker writes: a colour between two of an 8-bit format's
    // steps is the swatch it is written as, which is how Flux's label reads.
    private string Matched(Pick pick) =>
        UiColorValue.TryParse(pick.Text(Written), out var written) ? written.Hex : pick.Color.Hex;

    private static string Whole(double value) =>
        (double.IsNaN(value) ? 0 : (int)Math.Floor(value + 0.5)).ToString(CultureInfo.InvariantCulture);

    // A place along a surface as the runtime writes one: 0 to 1, at most four decimals.
    private static string Fraction(double value) =>
        (double.IsNaN(value) ? 0 : Math.Floor((Math.Clamp(value, 0, 1) * 10000) + 0.5) / 10000).ToString("0.####", CultureInfo.InvariantCulture);
}
