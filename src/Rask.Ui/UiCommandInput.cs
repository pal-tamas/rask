namespace Rask;

/// <summary>Flux UI's <c>flux:command.input</c>: the search box at the top of a <see cref="UiCommand" />.</summary>
/// <remarks>
/// <para>
/// What is typed narrows the list in the browser, and the box holds no value of the page's: nothing is sent while
/// the reader types. A command pressed empties it, as Flux's does.
/// </para>
/// <para>
/// Flux draws the clear button whether or not it is asked to (measured: the first example on its page gives no
/// <c>clearable</c> and has one), and only the close button when <see cref="Closable" /> is set. So the clear button
/// is there unless <see cref="Clearable" /> is <c>false</c>, or the box is closable and not also clearable.
/// </para>
/// </remarks>
public sealed partial class UiCommandInput : Component
{
    private static readonly UiPartMarker Marker = new("ui-command-input");

    private const string Field =
        "flex h-12 w-full items-center border-b border-zinc-200 bg-white py-2 ps-11 pe-11 text-base sm:text-sm font-medium text-zinc-800 "
        + "outline-none peer focus:ring-0 dark:border-zinc-600 dark:bg-zinc-700 dark:text-white";

    private const string Leading =
        "absolute top-0 bottom-0 start-0 flex items-center justify-center ps-3.5 text-xs text-zinc-400 has-[+input:focus]:text-zinc-800 dark:has-[+input:focus]:text-zinc-400";

    private const string Trailing = "absolute top-0 bottom-0 end-0 flex items-center justify-center pe-2";

    // Shown with the first character typed and gone with the last: the field's placeholder is shown exactly while
    // it is empty.
    private const string WhileTyped = "peer-placeholder-shown:hidden";

    private const string Action =
        "relative inline-flex items-center justify-center gap-2 size-8 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:bg-zinc-800/5 hover:text-zinc-800 dark:text-zinc-400 dark:hover:bg-white/15 dark:hover:text-white";

    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>A button that empties the box, shown while it holds something. Flux draws it unless told not to.</summary>
    public bool? Clearable { get; set; }

    /// <summary>A button that closes the <see cref="UiModal" /> the palette is in.</summary>
    public bool? Closable { get; set; }

    /// <summary>The icon at the start of the box. <see cref="Ui.IconName.MagnifyingGlass" /> unless this says otherwise.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>What the box says while it is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Classes for the <c>&lt;input&gt;</c>.</summary>
    public string? Class { get; set; }

    private string InputId => "ui-command-input-" + _instance.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var closable = Closable == true;
        var clearable = Clearable ?? !closable;

        return Div.Class("relative").Data(Marker.With(null))[
            Div.Class(Leading)[Ui.Icon.Name(Icon ?? Ui.IconName.MagnifyingGlass).Mini],
            Box(),
            clearable ? Div.Class(UiClass.Compose(Trailing, WhileTyped))[ClearButton()] : null,
            closable ? Div.Class(Trailing)[Div[CloseButton()]] : null
        ];
    }

    // No value and no handler: what is typed, and the command it leaves lit, are the browser's (rask-command.ts).
    // A space stands in for a missing placeholder, which is what tells the clear button the box is empty.
    private HTMLInputElement<string> Box() =>
        Input.Of<string>()
            .Id(InputId)
            .Type(InputType.Text)
            .Class(UiClass.Compose(Field, Class))
            .Placeholder(Placeholder ?? " ")
            .Autocomplete("off")
            .Aria("autocomplete", "none")
            .Attributes(UiRuntimeMarks.NotFormData);

    // The runtime empties the box in the click and tells it with an `input` (`data-rask-clear`), which narrows the
    // list again. Out of the tab order, as Flux's is.
    private Component ClearButton() =>
        Button
            .Type(ButtonType.Button)
            .Class(Action)
            .TabIndex(-1)
            .Aria("label", RaskStrings.Get(RaskString.CommandClear, "Clear command input"))
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-button"] = string.Empty, ["rask-clear"] = InputId })[
            Ui.Icon.Name(Ui.IconName.XMark).Micro
        ];

    // A close, as Ui.ModalClose's: the browser closes the dialog the palette is in.
    private static Component CloseButton()
    {
        var button = Button
            .Type(ButtonType.Button)
            .Class(Action)
            .Aria("label", RaskStrings.Get(RaskString.CommandClose, "Close command modal"))
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-button"] = string.Empty });

        return Context.Get<UiModalScope>() is { } modal
            ? button.Attributes(UiModalInvoker.Closes(modal.Id))[Ui.Icon.Name(Ui.IconName.XMark).Micro]
            : button[Ui.Icon.Name(Ui.IconName.XMark).Micro];
    }
}
