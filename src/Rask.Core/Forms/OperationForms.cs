using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;

namespace Rask.Core.Forms;

/// <summary>Makes the form over one operation for the generated <c>Form.For</c>: not an API to write against.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OperationForms
{
    /// <summary>The form of one operation, kept across renders where it stands, as a chain entry's is.</summary>
    /// <typeparam name="TInput">The form's model: the operation's parameters as fields.</typeparam>
    /// <param name="identity">The aggregate it is over — its id — so a form handed another one reads it afresh; null for a factory's.</param>
    /// <param name="load">Reads the aggregate into a new model, or null when no row has the id; a factory's makes an empty one.</param>
    /// <param name="submit">Runs the operation's entry with what the model holds.</param>
    /// <param name="may">Whether the signed-in principal may run the operation.</param>
    /// <returns>The form; its fields go in its indexer.</returns>
    public static OperationForm<TInput> Of<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TInput>(
        object? identity, Func<CancellationToken, Task<TInput?>> load, Func<TInput, CancellationToken, Task> submit, Func<bool> may)
        where TInput : class
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(submit);
        ArgumentNullException.ThrowIfNull(may);

        var form = LiveRenderContext.Walking is { } context
            ? context.GetOrCreateEntry(static _ => new OperationForm<TInput>(), static (_, _) => { }, 0UL, hasLifecycle: true, copy: static (_, _, _) => { })
            : new OperationForm<TInput>();
        form.Wire(identity, load, submit, may);
        return form;
    }
}
