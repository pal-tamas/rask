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
///         a stale version — shows each failure under its field as any form does, and one it accepts runs
///         <see cref="OnSaved" />. The operation's dry run is asked as each field is committed, so what the save would
///         refuse is told before it is tried.
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
        _missing = null;
    }

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
        return Form.Model(input).OnSubmit(Save)[
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
        if (_saved is { } saved)
        {
            await saved().ConfigureAwait(false);
        }

        if (_host is { } host)
        {
            await host.Saved().ConfigureAwait(false);
        }
    }
}
