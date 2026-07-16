using System.Collections.Concurrent;
using Microsoft.JSInterop;

namespace Rask.Example.Shared.Tests.Infrastructure;

internal sealed class FakeJsRuntime : IJSRuntime
{
    private readonly ConcurrentBag<(string Identifier, object?[]? Args)> _calls = new();
    private readonly Dictionary<string, Exception> _exceptions = new();
    private readonly Dictionary<string, Task> _pending = new();
    private readonly Dictionary<string, object?> _responses = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        _calls.Add((identifier, args));
        if (_exceptions.TryGetValue(identifier, out var ex))
        {
            return ValueTask.FromException<TValue>(ex);
        }

        // Every call completes synchronously by default, which quietly hides any bug that only exists
        // while a round trip is in flight (a real one crosses the wire and waits on the browser).
        // SetPending makes one identifier stay un-completed until the test releases it.
        if (_pending.TryGetValue(identifier, out var gate))
        {
            return new ValueTask<TValue>(gate.ContinueWith(
                _ => _responses.TryGetValue(identifier, out var p) && p is TValue t ? t : default!,
                TaskScheduler.Default));
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

    public void SetResponse(string identifier, object? response) => _responses[identifier] = response;

    public void SetException(string identifier, Exception ex) => _exceptions[identifier] = ex;

    /// <summary>
    ///     Hold <paramref name="identifier" />'s calls un-completed until <paramref name="gate" /> does —
    ///     for asserting on what happens while an interop round trip is still in flight.
    /// </summary>
    public void SetPending(string identifier, Task gate) => _pending[identifier] = gate;

    public IReadOnlyList<object?[]?> GetCalls(string identifier) =>
        _calls.Where(c => c.Identifier == identifier).Select(c => c.Args).ToArray();

    public int CallCount(string identifier) => _calls.Count(c => c.Identifier == identifier);
}
