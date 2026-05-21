using System.Collections.Concurrent;
using Microsoft.JSInterop;

namespace Rask.Example.Shared.Tests.Infrastructure;

internal sealed class FakeJsRuntime : IJSRuntime
{
    private readonly ConcurrentBag<(string Identifier, object?[]? Args)> _calls = new();
    private readonly Dictionary<string, object?> _responses = new();
    private readonly Dictionary<string, Exception> _exceptions = new();

    public void SetResponse(string identifier, object? response) => _responses[identifier] = response;

    public void SetException(string identifier, Exception ex) => _exceptions[identifier] = ex;

    public IReadOnlyList<object?[]?> GetCalls(string identifier) =>
        _calls.Where(c => c.Identifier == identifier).Select(c => c.Args).ToArray();

    public int CallCount(string identifier) => _calls.Count(c => c.Identifier == identifier);

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

        // Default-of-T for unconfigured calls: matches the behaviour of an empty
        // sessionStorage slot (string? returns null) and a void call (returns the
        // sentinel object the runtime ignores).
        return ValueTask.FromResult<TValue>(default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}
