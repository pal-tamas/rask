using Microsoft.JSInterop;

namespace Rask.Client.Tests;

// Minimal IJSRuntime recorder for asserting the Rask.Client browser-API wrappers against the exact
// dotted identifier + argument list they ship. Returns canned values for read calls.
internal sealed class FakeJsRuntime : IJSRuntime
{
    private readonly List<(string Identifier, object?[]? Args)> _calls = [];
    private readonly Dictionary<string, Exception> _exceptions = new();
    private readonly Dictionary<string, object?> _responses = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        _calls.Add((identifier, args));
        if (_exceptions.TryGetValue(identifier, out var ex))
        {
            return ValueTask.FromException<TValue>(ex);
        }

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

    public void SetException(string identifier, Exception ex) => _exceptions[identifier] = ex;

    public object?[]? ArgsFor(string identifier) => _calls.Single(c => c.Identifier == identifier).Args;

    public int CallCount(string identifier) => _calls.Count(c => c.Identifier == identifier);
}
