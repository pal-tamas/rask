using Microsoft.JSInterop;

namespace Rask.Testing;

/// <summary>One recorded JS interop call: the dotted <paramref name="Identifier" /> and its arguments.</summary>
/// <param name="Identifier">The identifier the component invoked, e.g. <c>"raskApi.clipboard.write"</c>.</param>
/// <param name="Args">The arguments passed, or <c>null</c> if none.</param>
public readonly record struct JSCall(string Identifier, object?[]? Args);

/// <summary>
///     An <see cref="IJSRuntime" /> for tests: it records every call and returns whatever you configure,
///     so a component that injects <c>IJSRuntime</c> can be unit-tested without a browser. Register it in
///     the provider you pass to <see cref="RaskTest.Render{T}(T, IServiceProvider)" />, drive the component,
///     then assert on the calls it made.
/// </summary>
/// <remarks>
///     Records and replays — nothing more. An unconfigured call returns <c>default</c> for its return type,
///     which matches a real absent value (a missing storage key reads back as <c>null</c>) and a void call.
///     Safe to call from multiple threads; <see cref="Calls" /> keeps invocation order.
/// </remarks>
public sealed class TestJSRuntime : IJSRuntime
{
    private readonly List<JSCall> _calls = [];
    private readonly Dictionary<string, Exception> _exceptions = [];
    private readonly Lock _gate = new();
    private readonly Dictionary<string, object?> _responses = [];

    /// <summary>Every call made so far, in invocation order.</summary>
    public IReadOnlyList<JSCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        Exception? failure;
        object? canned;
        bool hasCanned;
        lock (_gate)
        {
            _calls.Add(new JSCall(identifier, args));
            _exceptions.TryGetValue(identifier, out failure);
            hasCanned = _responses.TryGetValue(identifier, out canned);
        }

        if (failure is not null)
        {
            return ValueTask.FromException<TValue>(failure);
        }

        return hasCanned && canned is TValue typed
            ? ValueTask.FromResult(typed)
            : ValueTask.FromResult<TValue>(default!);
    }

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    /// <summary>Makes every call to <paramref name="identifier" /> return <paramref name="response" />.</summary>
    public void SetResponse(string identifier, object? response)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        lock (_gate)
        {
            _responses[identifier] = response;
        }
    }

    /// <summary>Makes every call to <paramref name="identifier" /> fault with <paramref name="ex" />.</summary>
    public void SetException(string identifier, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(ex);
        lock (_gate)
        {
            _exceptions[identifier] = ex;
        }
    }

    /// <summary>
    ///     The arguments of the one call to <paramref name="identifier" />. Throws if it was not called
    ///     exactly once — when several calls are expected, read <see cref="Calls" /> instead.
    /// </summary>
    public object?[]? ArgsFor(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var matches = Calls.Where(c => c.Identifier == identifier).ToArray();
        return matches.Length == 1
            ? matches[0].Args
            : throw new InvalidOperationException(
                $"Expected exactly one call to '{identifier}', but there were {matches.Length}. "
                + "Read Calls to assert against several.");
    }

    /// <summary>How many times <paramref name="identifier" /> was called.</summary>
    public int CallCount(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return Calls.Count(c => c.Identifier == identifier);
    }
}
