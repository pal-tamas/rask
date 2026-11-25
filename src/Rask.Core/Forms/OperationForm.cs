using System.Diagnostics.CodeAnalysis;
using Rask.Core.Components;

namespace Rask.Core.Forms;

/// <summary>
///     A form over one operation of an aggregate — <c>Form.For(Supply.Change, id)[f =&gt; [ … ]]</c> — whose fields are
///     the operation's parameters: filled from the aggregate as it is stored, checked against the domain's own rules
///     while it is filled in, and saved by the operation's generated entry at the version it was filled from.
/// </summary>
/// <remarks>
///     <para>
///         Made by the generated <c>Form.For</c> of each operation, never by hand. Until the aggregate is read it draws
///         nothing; an id no row has draws <see cref="Missing" />. A submit the entry refuses — a rule, a <c>Refuse</c>,
///         a stale version — shows each failure under its field as any form does, and one it accepts reads the
///         aggregate afresh (its new version, a new row's id) and runs <see cref="OnSaved" />. The operation's dry run is asked as each field is committed, so what the save would
///         refuse is told before it is tried.
///     </para>
///     <para>
///         The <c>&lt;form&gt;</c> it draws takes the steps a hand-written <c>Form.Model(m)</c> does —
///         <see cref="Id" />, <see cref="Class" />, <see cref="Style" />, <c>Data</c>, <c>Attributes</c>,
///         <see cref="ConfirmLeave" />, <see cref="OnInvalidSubmit" /> — and <see cref="Saved" /> says what its host
///         (<c>Ui.EditorDialog</c>) toasts once it is saved.
///     </para>
///     <para>
///         A principal the operation's roles do not admit (<c>Supply.MayChange</c>) sees the form with every control in
///         it disabled, the submit among them: the entry would refuse the save anyway.
///     </para>
/// </remarks>
/// <typeparam name="TInput">The form's model: the operation's parameters as fields.</typeparam>
[SkipFactory]
public sealed class OperationForm<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TInput> : Component
    where TInput : class
{
    private Func<CancellationToken, Task<TInput?>> _load = static _ => Task.FromResult<TInput?>(null);
    private Func<TInput, CancellationToken, Task> _submit = static (_, _) => Task.CompletedTask;
    private Func<bool> _may = static () => true;
    private Func<TInput, IEnumerable<Component?>> _fields = static _ => [];
    private Func<Task>? _saved;
    private Func<TInput, Task>? _invalid;
    private string? _savedMessage;
    private OperationFormLook _look = new();
    private Component? _missing;
    private IOperationFormHost? _host;
    private TInput? _input;
    private bool _loaded;
    private bool _mounted;
    private object? _identity;

    internal OperationForm()
    {
    }

    /// <summary>The fields, given the form's model: <c>[f =&gt; [Ui.Field.Bind(() =&gt; f.Name), Ui.Button.Submit["Save"]]]</c>.</summary>
    /// <param name="fields">The form's content over its model.</param>
    public OperationForm<TInput> this[Func<TInput, IEnumerable<Component?>> fields]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(fields);
            _fields = fields;
            return this;
        }
    }

    /// <summary>What is drawn when no aggregate has the id the form was given. Nothing by default.</summary>
    /// <param name="missing">The stand-in.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Missing(Component? missing)
    {
        _missing = missing;
        return this;
    }

    /// <summary>Runs once a submit has been saved: <c>.OnSaved(async () =&gt; await Close())</c>.</summary>
    /// <param name="saved">What follows a save.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> OnSaved(Func<Task> saved)
    {
        _saved = saved;
        return this;
    }

    /// <summary>What is said once a submit has been saved: <c>.Saved("Company saved.")</c>.</summary>
    /// <remarks>
    ///     A host the form is in (<c>Ui.EditorDialog</c>) toasts it instead of its own "Saved"; a form with no host
    ///     toasts it itself.
    /// </remarks>
    /// <param name="message">The toast's text.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Saved(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _savedMessage = message;
        return this;
    }

    /// <summary>Runs on a submit the form's own validation refuses, before the operation is tried: <c>.OnInvalidSubmit(m =&gt; …)</c>.</summary>
    /// <param name="invalid">What follows a refused submit, given the model as it was submitted.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> OnInvalidSubmit(Func<TInput, Task> invalid)
    {
        _invalid = invalid;
        return this;
    }

    /// <summary>Asks before the reader leaves with the form changed and not saved, as a hand form's does: <c>.ConfirmLeave("Leave without saving?")</c>.</summary>
    /// <param name="message">The question asked.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> ConfirmLeave(string? message)
    {
        _look.ConfirmLeave = message;
        return this;
    }

    /// <summary>The <c>&lt;form&gt;</c>'s <c>id</c>.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Id(string? id)
    {
        _look.Id = id;
        return this;
    }

    /// <summary>The <c>&lt;form&gt;</c>'s classes: <c>.Class("grid gap-4", wide ? "w-full" : null)</c>, null and blank parts left out.</summary>
    /// <param name="parts">The class names, in order.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Class(params ReadOnlySpan<string?> parts)
    {
        _look.Class = BuilderRuntime.JoinClasses(parts);
        return this;
    }

    /// <summary>The <c>&lt;form&gt;</c>'s inline <c>style</c>.</summary>
    /// <param name="style">The declarations.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Style(string? style)
    {
        _look.Style = style;
        return this;
    }

    /// <summary>Sets one <c>data-{name}</c> attribute on the <c>&lt;form&gt;</c>; a null value renders it bare.</summary>
    /// <param name="name">The part after <c>data-</c>.</param>
    /// <param name="value">The value, or null for a bare attribute.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Data(string name, string? value)
    {
        _look.Data = new AttrBag(name, value);
        return this;
    }

    /// <summary>Sets several <c>data-*</c> attributes on the <c>&lt;form&gt;</c> at once; an empty list sets none.</summary>
    /// <param name="pairs">Name/value pairs, each name the part after <c>data-</c>.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Data(params ReadOnlySpan<(string Name, string? Value)> pairs)
    {
        _look.Data = pairs.IsEmpty ? null : new AttrBag(pairs);
        return this;
    }

    /// <summary>Sets one attribute on the <c>&lt;form&gt;</c> verbatim — the escape hatch; a null value renders it bare.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value, or null for a bare attribute.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Attributes(string name, string? value)
    {
        _look.Attributes = new AttrBag(name, value);
        return this;
    }

    /// <summary>Sets several attributes on the <c>&lt;form&gt;</c> verbatim at once; an empty list sets none.</summary>
    /// <param name="pairs">Name/value pairs.</param>
    /// <returns>The same form.</returns>
    public OperationForm<TInput> Attributes(params ReadOnlySpan<(string Name, string? Value)> pairs)
    {
        _look.Attributes = pairs.IsEmpty ? null : new AttrBag(pairs);
        return this;
    }

    // Set by OperationForms.Of each render; the read model and whether it was read stay across renders.
    internal void Wire(
        object? identity, Func<CancellationToken, Task<TInput?>> load, Func<TInput, CancellationToken, Task> submit, Func<bool> may)
    {
        // Handed another aggregate where it stands: what was read is another one's, so it is read again.
        if (_mounted && !Equals(identity, _identity))
        {
            _loaded = false;
            _input = null;
            _ = Reload();
        }

        _identity = identity;
        _load = load;
        _submit = submit;
        _may = may;
        _saved = null;
        _invalid = null;
        _savedMessage = null;
        _missing = null;
        _look = new OperationFormLook();
    }

    // What the generated Form.For wired, asked without a render: the tests that drive a generated form read and save
    // through these, as OnMount and a submit do.
    internal Task<TInput?> Read(CancellationToken cancellationToken) => _load(cancellationToken);

    internal Task Submit(TInput input, CancellationToken cancellationToken) => _submit(input, cancellationToken);

    /// <inheritdoc />
    protected override async Task OnMount()
    {
        _mounted = true;
        _input = await _load(CancellationToken).ConfigureAwait(false);
        _loaded = true;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        _host = Context.Get<IOperationFormHost>();
        if (!_loaded)
        {
            return null;
        }

        if (_input is not { } input)
        {
            return _missing ?? _host?.Missing;
        }

        var fields = _fields(input);
        var form = _look.Applied(Form.Model(input).OnSubmit(Save));

        // Its fields commit as they are left: what the dry run refuses is told then, not with the next action.
        form.CommitsOnLeave = true;
        return (_invalid is { } invalid ? form.OnInvalidSubmit(invalid) : form)[
            _may() ? fields : [Fieldset.Disabled(true)[fields]]
        ];
    }

    private async Task Reload()
    {
        // Yields first: the render that handed the new id is still in flight.
        await Task.Yield();
        _input = await _load(CancellationToken).ConfigureAwait(false);
        _loaded = true;
        await StateHasChangedAsync().ConfigureAwait(false);
    }

    private async Task Save(TInput input)
    {
        await _submit(input, CancellationToken).ConfigureAwait(false);

        // Read again: the save moved the version on and gave a new row its id, so the next submit of this form is
        // neither refused as stale nor adds that row twice. A factory's form starts empty again.
        _input = await _load(CancellationToken).ConfigureAwait(false);
        if (_saved is { } saved)
        {
            await saved().ConfigureAwait(false);
        }

        if (_host is { } host)
        {
            await host.Saved(_savedMessage).ConfigureAwait(false);
        }
        else if (_savedMessage is { } message)
        {
            Toast.Success(message);
        }
    }
}
