using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

// Runtime shape-detector for the inline `Validate: Delegate?` callbacks on Form / Input /
// Select / Textarea. Two supported shapes, both under the one name `Validate`:
//   sync   — Validate<TValue> (TValue → IEnumerable<string>)
//   async  — Func<TValue, ValueTask<IEnumerable<string>>>   (the field's token is Current.Cancellation)
// The delegate is stored as `Delegate?` rather than two typed properties because the user
// picked the single-prop call-site shape (see plan: scalable-bubbling-flame.md). Dispatch
// uses DynamicInvoke — the same trim-suppression rationale as Form's OnSubmit (Form.cs).
internal static class DelegateValidator
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DynamicInvoke on user-supplied delegate. The delegate's target type and " +
                        "parameters are preserved by the user's call site; framework code only " +
                        "dispatches with the value the user wired up.")]
    public static IEnumerable<string> InvokeSync(Delegate d, object? value)
    {
        var result = d.DynamicInvoke(value);
        return result switch
        {
            null => Array.Empty<string>(),
            IEnumerable<string> messages => messages,
            _ => Array.Empty<string>()
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DynamicInvoke on user-supplied delegate. See InvokeSync for the rationale.")]
    public static async ValueTask<IEnumerable<string>> InvokeAsync(
        Delegate d, object? value, CancellationToken cancellationToken)
    {
        // The rule reads the field's token from the ambient scope; it starts inside it, so everything it
        // awaits carries the token past this frame.
        object? result;
        using (Ambient.Enter(cancellationToken))
        {
            result = d.DynamicInvoke(value);
        }

        switch (result)
        {
            case null:
                return Array.Empty<string>();
            case ValueTask<IEnumerable<string>> vt:
                return await vt.ConfigureAwait(false);
            case Task<IEnumerable<string>> task:
                return await task.ConfigureAwait(false);
            default:
                return Array.Empty<string>();
        }
    }

    // A delegate is async when it hands back an awaitable instead of the messages themselves.
    public static bool IsAsync(Delegate d) =>
        d.Method.ReturnType == typeof(ValueTask<IEnumerable<string>>)
        || d.Method.ReturnType == typeof(Task<IEnumerable<string>>);
}
