using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     One file, bound to a form's member: <c>Ui.FilePicker.Bind(() =&gt; f.Document)</c> — Flux's file upload and the
///     file it holds, under one label, with the field's message under it.
/// </summary>
/// <remarks>
///     <para>
///         It is <see cref="UiFileUpload" /> and <see cref="UiFileUploadDropzone" /> while it is empty, and the chosen
///         file's <see cref="UiFileItem" /> with a remove button once a file is picked. The file is read into memory as it
///         is picked (<see cref="PickedFile" />), so it is still there when the form is submitted; a file larger than
///         <see cref="MaxSize" /> is refused there and then, under the field.
///     </para>
///     <para>
///         A form over an operation that takes an <c>Attachment</c> draws one for that parameter from
///         <c>Ui.Field.Bind(() =&gt; f.Document)</c>: the file is stored with the row when the operation saves, and a file
///         the store refuses — too large, a type it does not allow — is told under this field.
///     </para>
/// </remarks>
public sealed partial class UiFilePicker : Component, IFormControl<PickedFile?>, IUiFormControl
{
    private string? _refused;

    /// <summary>The label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Help text between the label and the upload.</summary>
    public string? Description { get; set; }

    /// <summary>Flux's <c>badge</c>, beside the label.</summary>
    public string? Badge { get; set; }

    /// <summary>False: the message under it is left to a <c>Ui.Error</c> of your own.</summary>
    public bool? ShowValidation { get; set; }

    /// <summary>Takes no file and draws the upload muted.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The input's own <c>accept</c> list (<c>"application/pdf"</c>): a filter on the picker and nothing more.</summary>
    public string? Accept { get; set; }

    /// <summary>The most bytes a picked file may hold; <see cref="PickedFile.DefaultMaxSize" /> when unset.</summary>
    public long? MaxSize { get; set; }

    /// <summary>The dropzone's heading, in place of its own words.</summary>
    public string? Heading { get; set; }

    /// <summary>The dropzone's line under the heading: what may be dropped.</summary>
    public string? Text { get; set; }

    /// <summary>The input's id. Derived from the bound member or the label when unset.</summary>
    public string? Id { get; set; }

    /// <summary>Classes for the call site, added to the upload's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    public Expression<Func<PickedFile?>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<PickedFile?>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<PickedFile?> AfterBind { get; set; }

    /// <inheritdoc />
    public PickedFile? Value { get; set; }

    /// <inheritdoc />
    public Callback<PickedFile?> OnChange { get; set; }

    /// <inheritdoc />
    string IUiFieldControl.ControlId => UiFieldId.Derive(this, Id, Bind, Label);

    /// <inheritdoc />
    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    string? IUiFormControl.DescriptionTrailing => null;

    /// <inheritdoc />
    bool? IUiFormControl.Invalid => _refused is null ? null : true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve<PickedFile?>(this);
        var field = UiWithField.For(this, Label, Description, badge: Badge, invalid: _refused is not null, error: _refused);

        Component control = current is { } picked
            ? Ui.FileItem.Heading(picked.Name).Size(picked.Size).Invalid(_refused is not null).Actions(Removal(picked, accessor, context))
            : Ui.FileUpload.Id(field.ControlId).Accept(Accept).Disabled(Disabled).Class(Class)
                .OnFiles(files => Pick(files, accessor, context))[
                Ui.FileUploadDropzone.Heading(Heading).Text(Text).Inline(true)
            ];

        return field.Wrap(Div.Data("ui-file-picker", "")[control]);
    }

    private UiFileItemRemove? Removal(PickedFile picked, ExpressionAccessor.Accessor? accessor, EditContext? context) =>
        Disabled == true
            ? null
            : Ui.FileItemRemove.AriaLabel(RaskStrings.Get(RaskString.FileItemRemove, "Remove file") + ": " + picked.Name)
                .OnClick(() => Remove(accessor, context));

    // Read while the browser's handle is open, so the form still has it when it is submitted.
    private async Task Pick(IReadOnlyList<IRaskFile> files, ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var picked = await PickedFile.Read(files[0], MaxSize ?? PickedFile.DefaultMaxSize, CancellationToken).ConfigureAwait(false);
            _refused = null;
            await UiFormCommit.CommitAsync(this, accessor, context, picked).ConfigureAwait(false);
        }
        catch (IOException)
        {
            _refused = RaskStrings.Get(RaskString.FileTooLarge, "The file is too large.");
        }
    }

    private Task Remove(ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        _refused = null;
        return UiFormCommit.ClearAsync(this, accessor, context);
    }
}
