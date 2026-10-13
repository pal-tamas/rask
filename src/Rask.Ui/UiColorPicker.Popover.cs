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

    // The probe's box: one pixel wide under a pointer, two on the sheet.
    private static readonly string[] SheetBox = ["2 1", "1 1"];

    private static readonly Dictionary<string, string?> DialogShut = new(StringComparer.Ordinal)
    {
        ["rask-modal"] = "any",
        ["rask-modal-open"] = "false",
    };

    private static readonly Dictionary<string, string?> DialogOpen = new(DialogShut, StringComparer.Ordinal) { ["rask-modal-open"] = "true" };

    private static readonly Dictionary<string, string?> AreaMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-area"] = string.Empty,
        ["rask-drag"] = "x y",
        ["rask-press-keeps-focus"] = string.Empty,
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
    };

    private static readonly Dictionary<string, string?> ProbeMarks = new(StringComparer.Ordinal)
    {
        ["ui-color-picker-sheet"] = string.Empty,
        ["rask-measure"] = string.Empty,
    };

    private readonly ElementRef<HTMLInputElement> _popoverInput = new();

    private readonly List<ElementRef<HTMLButtonElement>> _swatchRefs = [];

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

        // The runtime shows and closes the dialog as the render says (a key opened it, a swatch closed the
        // sheet), lets Escape and a press outside dismiss it, and marks it `data-open` while it shows.
        return Dialog.Id(DialogId).Class(UiColorPickerLook.Dialog).Data(_open ? DialogOpen : DialogShut).OnToggle(Toggled)[
            Div.Class(UiColorPickerLook.FocusPlaceholder).Data("ui-focus-placeholder", string.Empty).TabIndex(0),
            Context.Provide(scope)[layout],
            // Flux asks the window how wide it is; the kit asks the stylesheet, through a box only it sizes.
            Span.Class(UiColorPickerLook.SheetProbe).Data(ProbeMarks).Aria("hidden", "true")[
                Input.Value(SheetBox[_sheet ? 0 : 1]).OnInput(Measured).Type(InputType.Hidden)
            ]
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
            // the box around it, where Flux hears it too, and the page takes what the sheet shows.
            Div.Class(UiColorPickerLook.Footer)[
                Div.Class("inline")[Footer(Ui.Button.Ghost, RaskStrings.Get(RaskString.ColorPickerCancel, "Cancel"))],
                Div.Class("inline").Role("button").TabIndex(0).OnClick(KeepAsync)[Footer(Ui.Button.Primary, RaskStrings.Get(RaskString.ColorPickerSelect, "Select"))]
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
        var color = _shown.Color;
        var (across, down) = (Fraction(color.Saturation / 100), Fraction(1 - (color.Brightness / 100)));

        return Div.Class(UiClass.Compose(UiColorPickerLook.Area, @class)).Data(Off ? AreaOff : AreaMarks)[
            Div.Class(UiColorPickerLook.AreaLayer).Style("background-color:" + color.HueCss).Data("ui-color-picker-area-hue", string.Empty),
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
                .Aria(AreaAria(color))
                .OnKeyDown(AreaKeyAsync),
            Input.Value(across + " " + down).OnInput(AreaDraggedAsync).Type(InputType.Hidden)
        ];
    }

    private static Dictionary<string, string?> AreaAria(UiColorValue color)
    {
        var saturation = Whole(color.Saturation);

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = RaskStrings.Get(RaskString.ColorPickerArea, "Saturation and brightness"),
            ["roledescription"] = RaskStrings.Get(RaskString.ColorPickerAreaRole, "2D color picker"),
            ["valuemin"] = "0",
            ["valuemax"] = "100",
            ["valuenow"] = saturation,
            ["valuetext"] = RaskStrings.Get(RaskString.ColorPickerAreaValue, "Saturation {0}%, brightness {1}%", saturation, Whole(color.Brightness)),
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
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = alpha ? RaskStrings.Get(RaskString.ColorPickerOpacity, "Opacity") : RaskStrings.Get(RaskString.ColorPickerHue, "Hue"),
        };
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
                            .Style("background:linear-gradient(to right, transparent, " + _shown.Color.OpaqueCss + ")")
                            .Data("ui-color-picker-slider-track", string.Empty)
                        : null,
                    Div.Class(UiColorPickerLook.SliderFill)[
                        Div.Class(UiColorPickerLook.SliderIndicator).Style("inset-inline:0 calc(100% - " + at + ")").Data("ui-slider-indicator", string.Empty)
                    ],
                    Div.Class(UiColorPickerLook.SliderThumb).Style("inset-inline-start:" + at).Data("ui-slider-thumb", string.Empty)[
                        // Bound on change, not on input: a render then moves the thumb's own input while it
                        // has focus, which is where a press on the track leaves it.
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
                            .Data("rask-big-step", "10")
                            .OnChange(text => ChannelTypedAsync(channel, text))
                    ]
                ]
            ],
            Off ? null : Input.Value(along).OnInput(at => ChannelDraggedAsync(channel, at)).Type(InputType.Hidden)
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
            marks["rask-drag-inset"] = _sheet ? "10" : "8";
            marks["rask-focus"] = rangeId;
        }

        return marks;
    }

    /// <summary>Flux's <c>flux:color-picker.input</c>: the popover's own text input.</summary>
    /// <param name="placeholder">Shown while there is no colour, in place of the picker's own.</param>
    /// <param name="class">Classes added to the input's own.</param>
    internal Component DrawInput(string? placeholder, string? @class = null) =>
        Input
            .Value(Said(_popoverInput))
            .Type(InputType.Text)
            .Ref(_popoverInput)
            .Placeholder(placeholder ?? Hint)
            .Disabled(Off)
            .Class(UiClass.Compose(UiColorPickerLook.PopoverInput, @class))
            .Aria("label", RaskStrings.Get(RaskString.ColorPickerValue, "Color value"))
            .Data("ui-color-picker-popover-input", string.Empty)
            .OnInput(text => Typing(text, _popoverInput))
            .OnChange(text => TypedAsync(text, _popoverInput))
            .OnKeyDown(e => TypingKey(e, _popoverInput, opens: false));

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
        while (_swatchRefs.Count < palette.Swatches.Count)
        {
            _swatchRefs.Add(new ElementRef<HTMLButtonElement>());
        }

        var picked = _shown.Empty ? -1 : palette.IndexOf(Matched(_shown));
        var stop = TabStop(palette);

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
            .Ref(_swatchRefs[index])
            .Class(UiColorPickerLook.Swatch)
            .Style("background-color:" + swatch.Value)
            .Disabled(Off)
            .Data(marks)
            .Role("option")
            .TabIndex(stop ? 0 : -1)
            .Aria(SwatchAria(picked, swatch.Named))
            .OnClick(() => PickedAsync(palette, index))
            .OnKeyDown(e => SwatchKeyAsync(palette, index, e));
    }

    private static Dictionary<string, string?> SwatchAria(bool picked, string label) => new(StringComparer.Ordinal)
    {
        ["selected"] = picked ? "true" : "false",
        ["label"] = label,
    };

    private int ChannelValue(Ui.ColorPickerChannel channel)
    {
        var value = channel == Ui.ColorPickerChannel.Alpha ? _shown.Color.Alpha * 100 : _shown.Color.Hue;

        return double.IsNaN(value) ? 0 : (int)Math.Floor(value + 0.5);
    }

    private static string Whole(double value) =>
        (double.IsNaN(value) ? 0 : (int)Math.Floor(value + 0.5)).ToString(CultureInfo.InvariantCulture);

    // A place along a surface as the runtime writes one: 0 to 1, at most four decimals.
    private static string Fraction(double value) =>
        (double.IsNaN(value) ? 0 : Math.Floor((Math.Clamp(value, 0, 1) * 10000) + 0.5) / 10000).ToString("0.####", CultureInfo.InvariantCulture);
}
