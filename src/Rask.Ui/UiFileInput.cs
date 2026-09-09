using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

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
/// </remarks>
public sealed partial class UiFileInput : Component, IFormControl<string>
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public new required string Label { get; set; }

    /// <summary>
    ///     The chosen files themselves, for reading or uploading. Fires in both modes, and with an empty
    ///     list when the reader cancels the picker — so check it before taking the first entry.
    ///     <para>
    ///         Never trust what arrives: a file's reported name, size and type all come from the client.
    ///         Re-check them on the server before storing anything.
    ///     </para>
    /// </summary>
    public Action<IReadOnlyList<RaskFile>>? OnFiles { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnFiles" />.</summary>
    public Func<IReadOnlyList<RaskFile>, Task>? OnFilesAsync { get; set; }

    /// <summary>Lets the reader choose more than one file. The value reports the first.</summary>
    public bool? Multiple { get; set; }

    /// <summary>
    ///     What the picker offers, as the HTML <c>accept</c> list — <c>"image/*"</c>, <c>".pdf,.docx"</c>.
    ///     A filter on the dialog and nothing more; check the type again where the file lands.
    /// </summary>
    public string? Accept { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a file input. The rest draw the
    ///     default rather than a class that does nothing.
    /// </summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>The name of the chosen file. Reported, never drawn — see the type's own remarks.</remarks>
    public string? Value { get; set; }

    /// <inheritdoc />
    public Action<string>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<string, Task>? OnChangeAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<string>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<string>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<string>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<string>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<string, Task>? AfterBindAsync { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, _) = UiFormCommit.Resolve<string>(this);

        // Of<string>() rather than Value(…) or Bind(…): both of those would render a `value` attribute,
        // which a browser rejects on a file input. So the write-back is driven from the file list here
        // instead of by Input<T>, and the box is left for the platform to fill.
        return Input
            .Of<string>()
            .OnFilesAsync(async files =>
            {
                OnFiles?.Invoke(files);

                if (OnFilesAsync is { } handler)
                {
                    await handler(files).ConfigureAwait(false);
                }

                var name = files.Count > 0 ? files[0].Name : string.Empty;
                await UiFormCommit.CommitAsync(this, acc, ctx, name).ConfigureAwait(false);
            })
            .Type(InputType.File)
            .Multiple(Multiple == true)
            .Accept(Accept ?? string.Empty)
            // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen
            // reader needs: a field that is visibly red and says nothing is half a message. It is
            // OMITTED rather than nulled — a null renders the attribute valueless, and a valueless
            // aria-invalid reads as "true", which would mark every field in the kit invalid.
            .Aria(Tone == UiTone.Error
                ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
                : new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "file-input validator",
                Tone is { } tone ? UiClassNames.FileInputTone(tone) : "",
                Variant is { } variant ? UiClassNames.FileInputVariant(variant) : "",
                Size is { } size ? UiClassNames.FileInputSize(size) : "",
                Class));
    }
}
