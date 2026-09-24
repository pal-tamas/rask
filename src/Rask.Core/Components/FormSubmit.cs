using System.ComponentModel;

namespace Rask.Core.Components;

/// <summary>
///     What a form's submit is doing right now, handed to the children so they can render it:
///     <c>Form.Model(m).OnSubmit(Save)[f =&gt; [ … ]]</c>.
/// </summary>
/// <remarks>
///     <code>
///     Form.Model(_model).OnSubmit(async m =&gt; await Product.Create(m))[f =&gt; [
///         Ui.Input.Bind(() =&gt; _model.Name).Label("Name"),
///         f.Error is not null ? Ui.Alert.Error["Something went wrong."] : null,
///         Ui.Button.Submit.Primary.Disabled(f.Submitting)[f.Submitting ? "Saving…" : "Save"],
///     ]]
///     </code>
///     <para>
///         Only the lambda's parameter is ever written, so the type itself stays out of completion.
///     </para>
/// </remarks>
/// <param name="Submitting">Whether a submit is in flight. False again however the handler ended.</param>
/// <param name="Error">
///     What the last submit threw, or <c>null</c> when it succeeded — cleared when the next one starts.
///     The exception itself, so the page decides what to say about it.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct FormSubmit(bool Submitting, Exception? Error);
