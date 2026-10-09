namespace Rask;

/// <summary>
///     Flux's <c>flux:file-upload.dropzone</c>: the dashed area inside a <see cref="UiFileUpload" /> that says
///     what to drop, and shows that an upload is running.
/// </summary>
/// <remarks>
///     It holds no input and no handler of its own — the upload around it does — and reads its states from
///     there: <c>data-dragging</c> and <c>data-loading</c> on the upload, the input being disabled. With
///     <see cref="WithProgress" /> the bar is as wide as <c>--ui-file-upload-progress</c> and the figure beside it
///     is <c>--ui-file-upload-progress-as-string</c>: Flux's two variables under the kit's prefix, which the
///     upload fills from the runtime's own (<c>--rask-progress</c>) while files are on their way.
/// </remarks>
public sealed partial class UiFileUploadDropzone : UiElement
{
    private const string Frame =
        "flex rounded-lg border-dashed border-zinc-200 bg-zinc-50 transition-colors "
        + "dark:border-white/10 dark:bg-white/10 "
        + "in-data-dragging:border-zinc-300 in-data-dragging:bg-zinc-100 "
        + "dark:in-data-dragging:border-white/20 dark:in-data-dragging:bg-white/15 "
        + "peer-disabled:pointer-events-none peer-disabled:opacity-75";

    private const string Stacked = "flex-col items-center justify-center border-2 px-16 py-10";

    private const string Beside = "items-center border py-4 ps-5 pe-8";

    private const string Glyph =
        "text-zinc-400 transition dark:text-white/60 in-data-dragging:text-zinc-800 dark:in-data-dragging:text-white";

    private const string Spinner = "absolute inset-0 text-zinc-800 opacity-0 transition dark:text-white";

    private const string HeadingLook =
        "cursor-default text-sm font-medium text-zinc-800 dark:text-white [input:disabled~*_&]:opacity-75";

    private const string Bar = "absolute inset-0 flex items-center gap-3 opacity-0 in-data-loading:opacity-100";

    private const string Figure =
        "font-medium tabular-nums after:content-(--ui-file-upload-progress-as-string) dark:text-white/70";

    private static readonly UiPartMarker Marker = new("ui-file-upload-dropzone");

    /// <summary>The line the area leads with.</summary>
    public string? Heading { get; set; }

    /// <summary>The smaller line under the heading: what is accepted, and how much.</summary>
    public string? Text { get; set; }

    /// <summary>The icon. A cloud with an arrow when unset.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>The compact layout: icon beside the words.</summary>
    public bool? Inline { get; set; }

    /// <summary>Shows a bar that fills in place of <see cref="Text" /> while files upload, instead of a spinner.</summary>
    public bool? WithProgress { get; set; }

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(Frame, Inline == true ? Beside : Stacked, Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override IEnumerable<Component?> RenderChildren()
    {
        var withBar = WithProgress == true;

        return
        [
            Div.Class(Inline == true ? "relative me-4" : "relative mb-4")[
                // Without the bar the icon gives way to a spinner while files upload.
                Ui.Icon.Name(Icon ?? Ui.IconName.CloudArrowUp).Solid
                    .Class(UiClass.Compose(Glyph, withBar ? null : "in-data-loading:opacity-0")),
                Ui.Icon.Name(Ui.IconName.Loading)
                    .Class(UiClass.Compose(Spinner, withBar ? null : "in-data-loading:opacity-100"))
            ],
            Div.Class(Inline == true ? "flex flex-col gap-1" : "flex flex-col items-center gap-2")[
                Heading is null ? null : Div.Class(HeadingLook)[Heading],
                Words(withBar)
            ],
        ];
    }

    private Component? Words(bool withBar)
    {
        if (Text is null)
        {
            return null;
        }

        var line = Div.Class(Inline == true
            ? "relative cursor-default text-xs text-zinc-500 dark:text-white/60"
            : "relative cursor-default text-sm text-zinc-500 dark:text-white/60");

        return withBar
            ? line[
                Div.Class(Bar)[
                    Div.Class("h-1 grow rounded-full bg-zinc-200 dark:bg-white/10")[
                        Div.Class("h-full w-(--ui-file-upload-progress) rounded-full bg-zinc-500 dark:bg-white")
                    ],
                    Div.Class(Figure)
                ],
                Span.Class("in-data-loading:opacity-0")[Text]
            ]
            : line[Text];
    }
}
