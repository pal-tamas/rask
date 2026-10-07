using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:file-upload</c>: the area that takes files — clicked, reached by keyboard, or dropped on —
///     around a <see cref="UiFileUploadDropzone" /> or any markup of your own.
/// </summary>
/// <remarks>
///     <para>
///     Where Flux binds a Livewire property with <c>wire:model</c>, the files come to the page through
///     <see cref="OnFiles" />: Rask's upload, the same one a plain file input uses. The page keeps what it wants
///     of them and draws a <see cref="UiFileItem" /> for each, as Flux's examples do from <c>$photos</c>.
///     </para>
///     <para>
///     It is a <c>&lt;label&gt;</c> around a real <c>&lt;input type="file"&gt;</c>, so a click anywhere opens the
///     picker and the input keeps the keyboard, with no script. While files are dragged over it the runtime
///     writes <c>data-dragging</c> on it (Flux's attribute) and the input is laid over the whole area, so the
///     drop is the browser's own. <c>data-loading</c>, Flux's mark for an upload in flight, is drawn when it is
///     there and written by nothing yet: the runtime marks a control waiting on a click or a submit, not on files.
///     </para>
/// </remarks>
public sealed partial class UiFileUpload : Component, IUiFormControl
{
    private const string Root = "relative block cursor-auto";

    // Out of sight at rest; over the whole area while a drag is above it, to take the drop. The states are
    // written `[[data-dragging]_&]:` rather than `in-data-dragging:` on purpose: that one weighs nothing
    // (:where), so an app whose own sheet emits `sr-only` or `border-zinc-200` after the kit's would undo it.
    private const string Receiver =
        "peer sr-only border-none [[data-dragging]_&]:inset-0 [[data-dragging]_&]:z-10 [[data-dragging]_&]:m-0 "
        + "[[data-dragging]_&]:size-full [[data-dragging]_&]:opacity-0 [[data-dragging]_&]:[clip-path:none]";

    private static readonly UiPartMarker Marker = new("ui-file-upload");

    private static readonly IReadOnlyDictionary<string, string?> Droppable = Marker.With(null, "rask-dropzone", null);

    private static readonly Dictionary<string, string?> ReceiverMarks = new(StringComparer.Ordinal)
    {
        ["data-slot"] = "receiver",
    };

    /// <summary>The input's <c>name</c>, for a form that is posted.</summary>
    public string? Name { get; set; }

    /// <summary>Lets the reader choose, or drop, more than one file.</summary>
    public bool? Multiple { get; set; }

    /// <summary>Wraps the upload in a field with this label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Help text between the label and the upload.</summary>
    public string? Description { get; set; }

    /// <summary>A message to show under the upload, which also marks it invalid.</summary>
    public string? Error { get; set; }

    /// <summary>Takes no click, no key and no drop, and draws the dropzone muted.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     The files chosen or dropped — Rask's stand-in for <c>wire:model</c>. Each is readable only while the
    ///     handler runs, and its name, size and type are the browser's word: check them where the file lands.
    /// </summary>
    public Callback<IReadOnlyList<IRaskFile>> OnFiles { get; set; }

    /// <summary>The input's own <c>accept</c> list (<c>"image/*"</c>): a filter on the picker and nothing more.</summary>
    public string? Accept { get; set; }

    /// <summary>The input's id. Derived from the label when unset.</summary>
    public string? Id { get; set; }

    /// <summary>Classes for the call site, added to the upload's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    string IUiFieldControl.ControlId => UiFieldId.Derive(Id, null, Label);

    /// <inheritdoc />
    LambdaExpression? IUiFieldControl.Bound => null;

    /// <inheritdoc />
    string? IUiFormControl.DescriptionTrailing => null;

    /// <inheritdoc />
    string? IUiFormControl.Badge => null;

    /// <inheritdoc />
    bool? IUiFormControl.Invalid => null;

    /// <inheritdoc />
    bool? IUiFormControl.ShowValidation => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this, Label, Description, error: Error);

        // RaskMarkup.Label, qualified: this type's Label property hides the chain entry of the same name.
        return field.Wrap(
            RaskMarkup.Label
                .Class(UiClass.Compose(Root, Class))
                .Data(Disabled == true ? Marker.With(null) : Droppable)[
                Input
                    .Of<string>()
                    .Id(field.ControlId)
                    .Name(Name)
                    .Type(InputType.File)
                    .Multiple(Multiple == true)
                    .Accept(Accept)
                    .Disabled(Disabled == true)
                    .OnFiles(OnFiles)
                    .Aria(Named(field))
                    .Attributes(ReceiverMarks)
                    .Class(Receiver),
                Children ?? []
            ]);
    }

    // The field's label alone names the input, as on Flux's: without this the words of the dropzone, which the
    // <label> around it also holds, would be read out as part of the name.
    private IReadOnlyDictionary<string, string?> Named(UiWithField field) =>
        Label is null
            ? field.Aria
            : new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal) { ["labelledby"] = field.LabelId };
}
