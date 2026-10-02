using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core.Forms;

namespace Rask.Web.Tests;

// The browser, as far as Rask.Web can tell: every call it makes (identifier and arguments), answered with canned JSON,
// and each object it keeps as a handle that records its disposal. Enter() makes it the page the current event is on.
internal sealed class FakeBrowser : IJSRuntime
{
    private readonly Queue<string> _answers = new();

    public List<(string Identifier, object?[] Args)> Calls { get; } = [];

    public List<FakeHandle> Kept { get; } = [];

    // The JSON the next run answers with, in order.
    public FakeBrowser Answers(params string[] json)
    {
        foreach (var j in json)
        {
            _answers.Enqueue(j);
        }

        return this;
    }

    public IDisposable Enter() => Enter(this);

    // Any runtime as the page the current event is on.
    public static IDisposable Enter(IJSRuntime runtime) => DispatchServicesScope.Push(new Page(runtime));

    public string Steps(int call) => (string)Calls[call].Args[1]!;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add((identifier, args ?? []));
        if (typeof(TValue) == typeof(IJSObjectReference))
        {
            var handle = new FakeHandle();
            Kept.Add(handle);
            return ValueTask.FromResult((TValue)(object)handle);
        }

        // A call that answers nothing (InvokeVoidAsync) takes no answer from the queue.
        if (string.Equals(typeof(TValue).Name, "IJSVoidResult", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(default(TValue)!);
        }

        var json = _answers.Count > 0 ? _answers.Dequeue() : "null";
        return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    // The page's services, as far as Rask.Web asks: its runtime.
    private sealed class Page(IJSRuntime runtime) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IJSRuntime) ? runtime : null;
    }

    internal sealed class FakeHandle : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();
    }
}
