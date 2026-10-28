namespace Rask;

/// <summary>
/// The field-shaped button that shows a datetime picker's choice and opens it — Flux UI's
/// <c>flux:datetime-picker.button</c>.
/// </summary>
/// <remarks>
/// What <c>Ui.DatetimePicker</c> draws by default. Write it into the picker's <c>Trigger</c> slot to size it or give
/// it its own placeholder; it reads the choice and the popup from the picker around it.
/// </remarks>
public sealed partial class UiDatetimePickerButton : Component
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-group-target"] = "",
        ["ui-datetime-picker-button"] = "",
    };

    private static readonly Dictionary<string, string?> MarksInvalid = new(Marks, StringComparer.Ordinal) { ["invalid"] = "" };

    private static readonly Dictionary<string, string?> PlaceholderMark = new(StringComparer.Ordinal)
    {
        ["ui-datetime-picker-placeholder"] = "",
    };

    /// <summary>What the button shows while nothing is chosen. The picker's own unless set.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall the button is.</summary>
    public Ui.DatetimePickerButtonSize? Size { get; set; }

    /// <summary>A button that empties the picker, shown while something is chosen. The picker's own unless set.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Draws the button as holding an error.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Extra classes for the button.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiDatetimePickerScope>() is not { } scope)
        {
            return null;
        }

        var placeholder = Placeholder ?? scope.Placeholder;
        var aria = new Dictionary<string, string?>(scope.Aria, StringComparer.Ordinal)
        {
            ["label"] = placeholder,
            ["haspopup"] = "dialog",
            ["controls"] = scope.PopoverId,
            // The browser's while the popup shows (rask-toggle.ts).
            ["expanded"] = "false",
        };

        var height = UiDatePickerLook.Height((int)(Size ?? Ui.DatetimePickerButtonSize.Base));
        var button = Button
            .Id(scope.ControlId)
            .Type(ButtonType.Button)
            .Class(UiDatePickerLook.Button, height, Class)
            .Data(new Dictionary<string, string?>(Invalid == true || scope.Invalid ? MarksInvalid : Marks, StringComparer.Ordinal)
            {
                // `data-open` while the popup shows, as Flux writes it (rask-toggle.ts).
                [UiInvoker.OpenMark] = scope.PopoverId,
            })
            .Disabled(scope.Disabled)
            .Aria(aria)
            .Attributes(("popovertarget", scope.PopoverId))[
            Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiDatePickerLook.Leading),
            Div.Class(UiDatePickerLook.Selected).Attributes(UiCalendarHook.Named("text"))[
                Words(scope, placeholder)
            ],
            Clearable ?? scope.Clearable
                ? UiPickerClear.Inside(scope.Clear, scope.Text is not null && !scope.Disabled,
                    RaskStrings.Get(RaskString.DatetimePickerClear, "Clear date and time"), UiCalendarHook.Named("clear"))
                : null,
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiDatePickerLook.Trailing)
        ];

        return button;
    }

    // The date, a grey "at" and the time (or the moment alone); a range's two ends and the dash between them; or the placeholder.
    private static Component Words(UiDatetimePickerScope scope, string placeholder)
    {
        if (scope.Text is not var (before, between, after))
        {
            // Each under its own key: a span against a div is no change the live diff can patch.
            return Span.Key("placeholder").Class(UiDatetimePickerLook.Placeholder).Data(PlaceholderMark)[placeholder];
        }

        return Div.Key("chosen").Class(UiDatetimePickerLook.Words).Attributes(("dir", "auto"))[before, between.Length == 0 ? null : Span.Class(UiDatetimePickerLook.At)[between], after];
    }
}
