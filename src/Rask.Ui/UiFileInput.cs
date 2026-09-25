using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A file picker.
/// </summary>
/// <remarks>
/// <para>
/// A form control over a <c>string</c> — the chosen file's NAME, which is the only part of a file input
/// a page is allowed to read as a value and the part a plain form posts. The bytes come through
/// <see cref="OnFiles" />, which fires in both modes.
/// </para>
/// <para>
/// The one place this control differs from the rest of the kit: <b>bound mode is write-only.</b> A
/// browser refuses to have a file input's value SET, for the obvious reason — a page that could would be
/// a page that can read any file on the disk by guessing its path. So binding here fills the model from
/// the reader's choice and drives validation, and a model value never draws a filename back into the
/// box. That is the platform's rule, not a gap.
/// </para>
/// <para>
/// <see cref="Dropzone" /> draws Flux UI's drop area instead of the compact box, and it is still the same
/// native input: stretched invisibly over the whole area, so a click anywhere opens the picker and a file
/// dropped anywhere lands in the input the way the browser already handles a drop on one. No script decides
/// where a drop goes, so it works before the runtime boots; the runtime only marks the area while a file is
/// dragged over it, because no CSS state says "something is being dragged here".
/// </para>
/// </remarks>
public sealed partial class UiFileInput : UiFormField<string>
{

    /// <summary>
    ///     The chosen files themselves, for reading or uploading. Fires in both modes, and with an empty
    ///     list when the reader cancels the picker — so check it before taking the first entry.
    ///     <para>
    ///         Never trust what arrives: a file's reported name, size and type all come from the client.
    ///         Re-check them on the server before storing anything.
    ///     </para>
    /// </summary>
    public Callback<IReadOnlyList<RaskFile>> OnFiles { get; set; }


    /// <summary>Lets the reader choose more than one file. The value reports the first.</summary>
    public bool? Multiple { get; set; }

    /// <summary>
    ///     What the picker offers, as the HTML <c>accept</c> list — <c>"image/*"</c>, <c>".pdf,.docx"</c>.
    ///     A filter on the dialog and nothing more; check the type again where the file lands.
    /// </summary>
    public string? Accept { get; set; }

    /// <summary>
    ///     Draws a large area to drop files on, or click, in place of the compact box. The words in it come
    ///     from <see cref="Heading" /> and <see cref="Text" />.
    /// </summary>
    public bool? Dropzone { get; set; }

    /// <summary>
    ///     The line the drop area leads with. Defaults to <see cref="UiFormField{T}.Label" />, which is also the input's
    ///     accessible name — so a heading that says what to drop keeps both halves of the message.
    /// </summary>
    public string? Heading { get; set; }

    /// <summary>
    ///     The smaller line under the heading — what is accepted and how much. Linked to the input as its
    ///     description, since the input is what a screen reader lands on and the words are drawn beside it
    ///     rather than inside it.
    /// </summary>
    public string? Text { get; set; }




    /// <inheritdoc />
    // The dropzone's heading IS its caption, so the field draws no legend over it.
    private protected override bool LabelsItself => Dropzone == true;

    /// <inheritdoc />
    protected override Component Control()
    {
        var (acc, ctx, _) = UiFormCommit.Resolve<string>(this);

        // Of<string>() rather than Value(…) or Bind(…): both of those would render a `value` attribute,
        // which a browser rejects on a file input. So the write-back is driven from the file list here
        // instead of by Input<T>, and the box is left for the platform to fill.
        var input = Input
            .Of<string>()
            .Id(FieldId)
            .OnFiles(async files =>
            {
                await OnFiles.Invoke(files).ConfigureAwait(false);

                var name = files.Count > 0 ? files[0].Name : string.Empty;
                await UiFormCommit.CommitAsync(this, acc, ctx, name).ConfigureAwait(false);
            })
            .Type(InputType.File)
            .Multiple(Multiple == true)
            .Accept(Accept ?? string.Empty)
            .Aria(Aria())
            .Disabled(Disabled == true);

        if (Dropzone != true)
        {
            return input.Class(UiClass.Compose(
                "file-input validator",
                Tone is { } tone ? UiClassNames.FileInputTone(tone) : "",
                Variant is { } variant ? UiClassNames.FileInputVariant(variant) : "",
                Size is { } size ? UiClassNames.FileInputSize(size) : "",
                Class));
        }

        // The input is the WHOLE area, transparent and on top: a click anywhere is a click on it, and a file
        // dropped anywhere is dropped on it, which every engine already turns into a chosen file and a change
        // event. `validator` stays so a following Ui.Validator still reads its aria-invalid.
        return Div
            .Data(new Dictionary<string, string?> { ["rask-dropzone"] = null })
            .Class(UiClass.Compose(
                "relative flex flex-col items-center justify-center gap-1 rounded-box border-2 border-dashed "
                + "bg-base-100 px-6 py-8 text-center transition-colors",
                Tone == Ui.Tone.Error ? "border-error" : "border-base-300",
                Disabled == true
                    ? "opacity-60"
                    : "hover:bg-base-200 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 "
                      + "has-[:focus-visible]:outline-primary data-[dragging]:border-primary "
                      + "data-[dragging]:bg-primary/5",
                Class))[
            Ui.Icon.Name(Ui.IconName.Upload).Class("mb-1 size-8 text-ui-muted"),
            P.Class("text-sm font-medium")[Heading ?? Label ?? AccessibleLabel, BadgeFor()],
            Text is null
                ? null
                : P.Id(TextId).Class("text-xs text-ui-muted")[Text],
            input.Class(Disabled == true
                ? "validator absolute inset-0 size-full cursor-not-allowed opacity-0"
                : "validator absolute inset-0 size-full cursor-pointer opacity-0")
        ];
    }

    private string TextId => FieldId + "-text";

    // The field's name, invalid state and description from the base, as every kit field has them. A dropzone
    // draws no legend (LabelsItself), so its visible heading cannot name the input through a <label>: the Label
    // becomes its accessible name directly, and the dropzone's own text describes it ahead of the field's
    // hint and messages.
    private Dictionary<string, string?> Aria()
    {
        var aria = ControlAria();
        if (Dropzone != true)
        {
            return aria;
        }

        if (Label is { } label)
        {
            aria["label"] = label;
        }

        if (Text is not null)
        {
            aria["describedby"] = aria.TryGetValue("describedby", out var more) && more is not null
                ? TextId + " " + more
                : TextId;
        }

        return aria;
    }
}
