using Microsoft.JSInterop;

namespace Rask.Core.Tests.Interop;

// Records every IJSRuntime call (identifier + args) so the browser-API wrappers can be asserted
// against the exact dotted identifier and argument list they ship — the contract the client-side
// dispatcher and the framework JS helpers depend on. Returns canned values for read calls.
internal sealed class FakeJsRuntime : IJSRuntime
{
    private readonly List<(string Identifier, object?[]? Args)> _calls = [];
    private readonly Dictionary<string, object?> _responses = new();

    public IReadOnlyList<(string Identifier, object?[]? Args)> Calls => _calls;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        _calls.Add((identifier, args));
        if (_responses.TryGetValue(identifier, out var canned) && canned is TValue typed)
        {
            return ValueTask.FromResult(typed);
        }

        return ValueTask.FromResult<TValue>(default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    public void SetResponse(string identifier, object? response) => _responses[identifier] = response;

    public object?[]? ArgsFor(string identifier) =>
        _calls.Single(c => c.Identifier == identifier).Args;

    public int CallCount(string identifier) => _calls.Count(c => c.Identifier == identifier);
}
