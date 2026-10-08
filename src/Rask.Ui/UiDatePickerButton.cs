namespace Rask;

/// <summary>
/// The field-shaped button that shows a date picker's choice and opens its calendar — Flux UI's
/// <c>flux:date-picker.button</c>.
/// </summary>
/// <remarks>
/// What <c>Ui.DatePicker</c> draws by default. Write it into the picker's <c>Trigger</c> slot to size it or give
/// it its own placeholder; it reads the choice, the popup and the open state from the picker around it.
/// </remarks>
public sealed partial class UiDatePickerButton : Component
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-group-target"] = "",
        ["ui-date-picker-button"] = "",
    };

    private static readonly Dictionary<string, string?> MarksInvalid = new(Marks, StringComparer.Ordinal) { ["invalid"] = "" };

    private static readonly Dictionary<string, string?> PlaceholderMark = new(StringComparer.Ordinal)
    {
        ["ui-date-picker-placeholder"] = "",
    };

    /// <summary>What the button shows while nothing is chosen. The picker's own unless set.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall the button is.</summary>
    public Ui.DatePickerButtonSize? Size { get; set; }

    /// <summary>Prevents the reader from opening the picker.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Draws the button as holding an error.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Extra classes for the button.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiDatePickerScope>() is not { } scope)
        {
            return null;
        }

        var placeholder = Placeholder ?? scope.Placeholder;
        var aria = new Dictionary<string, string?>(scope.Aria, StringComparer.Ordinal)
        {
            ["label"] = placeholder,
            ["controls"] = scope.CalendarId,
            ["haspopup"] = "listbox",
            ["expanded"] = scope.Open ? "true" : "false",
        };

        return Button
            .Id(scope.ControlId)
            .Type(ButtonType.Button)
            .Class(UiClass.Compose(UiDatePickerLook.Button, UiDatePickerLook.Height((int)(Size ?? Ui.DatePickerButtonSize.Base)), Class))
            .Data(Invalid == true || scope.Invalid ? MarksInvalid : Marks)
            .Role("combobox")
            .Disabled(Disabled == true || scope.Disabled)
            .Aria(aria)
            // Enter and Space press the button, which is the browser's; the arrows open it too, as on Flux, and
            // the runtime keeps them from scrolling the page on their way.
            .Attributes(("popovertarget", scope.PopoverId), ("data-rask-contain-keys", "ArrowUp ArrowDown"))
            .OnKeyDown(e =>
            {
                if (e.Key is Keys.ArrowDown or Keys.ArrowUp)
                {
                    scope.Show();
                }
            })[
            Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiDatePickerLook.Leading),
            Div.Class(UiDatePickerLook.Selected)[
                scope.Text is { } text ? Span[text] : Span.Class(UiDatePickerLook.Placeholder).Data(PlaceholderMark)[placeholder]
            ],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiDatePickerLook.Trailing)
        ];
    }
}
