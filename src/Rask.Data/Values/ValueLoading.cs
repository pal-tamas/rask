using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     A value object being rebuilt from what was sent, as a row is loaded: while it is open, the value objects made on
///     this thread do not ask their rules. A generated wire codec opens one through
///     <see cref="GeneratedValueRules.Loading" /> around the <c>Create</c> that rebuilds a value it read; not an API to
///     write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly ref struct ValueLoading
{
    private readonly Loading.Scope _scope;

    private ValueLoading(Loading.Scope scope) => _scope = scope;

    /// <summary>Closes the scope: a value made after this checks its rules again.</summary>
    public void Dispose() => _scope.Dispose();

    internal static ValueLoading Begin() => new(Loading.Begin());
}
