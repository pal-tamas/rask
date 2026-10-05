using Microsoft.JSInterop;

namespace Rask.Core.Tests.Interop;

// A handle to an object the browser holds (a MediaStream a trigger or a peer handed over), as FakeJsRuntime answers
// one: it remembers being disposed of, and nothing else.
internal sealed class FakeJsObject : IJSObjectReference
{
    public bool Disposed { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult<TValue>(default!);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        ValueTask.FromResult<TValue>(default!);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }
}
