using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Web.Types;

namespace Rask.Web;

/// <summary>
///     A path through the browser's objects — property reads and method calls from the window, or from an object a chain
///     kept — that the generated members of Rask.Web build and run in one round trip when awaited.
/// </summary>
/// <remarks>
///     The steps cross as one JSON string, written with Rask.Web's own trim-safe metadata, so neither host's runtime has
///     to know Rask.Web's types: the runtime carries a string there and a <see cref="JsonElement" /> back. What JSON
///     cannot carry — a C# handler, a kept object, an element — rides beside the steps as its own argument, which the
///     host revives, and the steps name it by position: <c>{"__raskArg__": 0}</c>.
/// </remarks>
internal sealed class JsChain
{
    internal const char StepRoot = '\0';
    internal const char StepRead = 'g';
    internal const char StepCall = 'c';
    internal const char StepWrite = 's';
    internal const char StepNew = 'n';

    // What JSInterop's InvokeAsync<T> asks of a result type, so the trimmer keeps it deserializable.
    private const DynamicallyAccessedMemberTypes Json =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;

    private readonly JsChain? _parent;
    private readonly char _kind;
    private readonly string? _name;
    private readonly object?[]? _args;

    // Set on a root only: a kept object and the runtime it lives in, or, kept from a fake, the path it stands for.
    private readonly IJSObjectReference? _handle;
    private readonly IJSRuntime? _runtime;
    private readonly IReadOnlyList<Step>? _faked;

    private JsChain(
        JsChain? parent, char kind, string? name, object?[]? args, IJSObjectReference? handle = null, IJSRuntime? runtime = null,
        IReadOnlyList<Step>? faked = null)
    {
        _parent = parent;
        _kind = kind;
        _name = name;
        _args = args;
        _handle = handle;
        _runtime = runtime;
        _faked = faked;
    }

    // One step of a path, as a fake matches it: `matchMedia("(min-width: 900px)")` is not `matchMedia("print")`.
    internal readonly record struct Step(char Kind, string Name, object?[]? Args)
    {
        public bool Matches(Step other) =>
            Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && (Args ?? []).SequenceEqual(other.Args ?? []);
    }

    internal static JsChain Window { get; } = new(null, StepRoot, null, null);

    // A chain that is an object the browser holds for us rather than a path to one.
    internal bool IsKept => _kind == StepRoot && (_handle is not null || _faked is not null);

    internal JsChain Get(string name) => new(this, StepRead, name, null);

    internal JsChain Invoke(string name) => new(this, StepCall, name, null);

    internal JsChain Invoke(string name, object?[] args) => new(this, StepCall, name, args);

    // `new window[name](…args)`: a constructor, which only ever makes a new object, so it is always kept.
    internal JsChain New(string name) => new(this, StepNew, name, null);

    internal JsChain New(string name, object?[] args) => new(this, StepNew, name, args);

    internal ValueTask<T> Read<[DynamicallyAccessedMembers(Json)] T>(string name) => Get(name).Run<T>();

    // Waits for a promise the object holds (a view transition's `finished`).
    internal ValueTask Settle(string name) => Get(name).Run();

    internal ValueTask Call(string name) => Invoke(name).Run();

    internal ValueTask Call(string name, object?[] args) => Invoke(name, args).Run();

    internal ValueTask<T> Call<[DynamicallyAccessedMembers(Json)] T>(string name) => Invoke(name).Run<T>();

    internal ValueTask<T> Call<[DynamicallyAccessedMembers(Json)] T>(string name, object?[] args) => Invoke(name, args).Run<T>();

    internal ValueTask Write(string name, object? value) => new JsChain(this, StepWrite, name, [value]).Run();

    // Runs the chain and keeps what it ends at, a handle to an object the browser holds until it is disposed of.
    internal async ValueTask<JsChain> Keep()
    {
        if (Faked(out _, out _))
        {
            return new JsChain(null, StepRoot, null, null, faked: Path());
        }

        var runtime = Runtime;
        var handle = await runtime.InvokeAsync<IJSObjectReference>("__raskWeb.run", Arguments()).ConfigureAwait(false);
        return new JsChain(null, StepRoot, null, null, handle, runtime);
    }

    internal async ValueTask<T> Keep<T>(Func<JsChain, T> wrap) => wrap(await Keep().ConfigureAwait(false));

    // Whether the browser has what the chain ends at.
    internal ValueTask<bool> Exists() =>
        _kind == StepRoot || Faked(out _, out _) ? ValueTask.FromResult(true) : Runtime.InvokeAsync<bool>("__raskWeb.has", Arguments());

    internal ValueTask Release() => _handle?.DisposeAsync() ?? default;

    // Adds `handler` to the object the chain ends at, for events of `type`; each one arrives as the fields named in
    // `fields` (a JSON array: only what the payload type reads), read into a TEvent. Disposing of what this returns
    // removes it; so does the owner unmounting.
    internal async ValueTask<IAsyncDisposable> Listen<TEvent>(
        string type, string fields, Delegate handler, Func<JsonElement, TEvent> read, Func<TEvent, Task> invoke)
    {
        if (Faked(out var fake, out _))
        {
            var listener = new WebFakes.Listener(type, Owner(handler), e => invoke((TEvent)e));
            fake.Listeners.Add(listener);
            return new FakeListening(fake, listener);
        }

        var callback = ScopedScript.Handler(Owner(handler), args => invoke(read(First(args))));
        var runtime = Runtime;
        var (steps, extras) = Serialize();
        int id;
        try
        {
            id = await runtime.InvokeAsync<int>("__raskWeb.listen", [Start._handle, steps, type, fields, callback, .. extras]).ConfigureAwait(false);
        }
        catch
        {
            ScopedScript.Release(callback);
            throw;
        }

        return new Listening(runtime, id, callback);
    }

    // A C# handler as a function the browser can call: runs in its component's order and re-renders it. Released when
    // the component unmounts.
    internal static object? Callback(Action? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), _ => Done(handler));

    internal static object? Callback(Func<Task>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), _ => handler());

    internal static object? Callback<[DynamicallyAccessedMembers(Json)] T1>(Action<T1>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => Done(() => handler(Arg<T1>(a, 0))));

    internal static object? Callback<[DynamicallyAccessedMembers(Json)] T1>(Func<T1, Task>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => handler(Arg<T1>(a, 0)));

    internal static object? Callback<[DynamicallyAccessedMembers(Json)] T1, [DynamicallyAccessedMembers(Json)] T2>(Action<T1, T2>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => Done(() => handler(Arg<T1>(a, 0), Arg<T2>(a, 1))));

    internal static object? Callback<[DynamicallyAccessedMembers(Json)] T1, [DynamicallyAccessedMembers(Json)] T2, [DynamicallyAccessedMembers(Json)] T3>(
        Action<T1, T2, T3>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => Done(() => handler(Arg<T1>(a, 0), Arg<T2>(a, 1), Arg<T3>(a, 2))));

    // One field of an event's payload, as the generated payload types read it.
    internal static T Field<[DynamicallyAccessedMembers(Json)] T>(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) ? Value<T>(value) : default!;

    private static T Arg<[DynamicallyAccessedMembers(Json)] T>(JsonElement args, int index) =>
        args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > index ? Value<T>(args[index]) : default!;

    private static T Value<[DynamicallyAccessedMembers(Json)] T>(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? default! : value.Deserialize(TypeInfo<T>())!;

    private static JsonElement First(JsonElement args) =>
        args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0 ? args[0] : default;

    private static Task Done(Action run)
    {
        run();
        return Task.CompletedTask;
    }

    // The component a handler belongs to: the one it re-renders, whose order it runs in, and whose unmount releases it.
    private static Component Owner(Delegate handler) =>
        DelegateOwner.Resolve(handler) ?? throw new InvalidOperationException(
            "A handler handed to the browser has to belong to a component — a lambda written in one, or a method of it — " +
            "since it runs in that component's order and re-renders it.");

    private async ValueTask<T> Run<[DynamicallyAccessedMembers(Json)] T>()
    {
        if (Faked(out var fake, out var rest))
        {
            return fake.Answer<T>(rest);
        }

        var result = await Runtime.InvokeAsync<JsonElement>("__raskWeb.run", Arguments()).ConfigureAwait(false);
        return Value<T>(result);
    }

    private ValueTask Run()
    {
        if (Faked(out var fake, out var rest))
        {
            fake.Answer<object>(rest);
            return default;
        }

        return Runtime.InvokeVoidAsync("__raskWeb.run", Arguments());
    }

    // The fake standing in for where this chain goes, if a test set one up.
    private bool Faked(out WebFakes.Entry fake, out IReadOnlyList<Step> rest)
    {
        fake = null!;
        rest = [];
        return WebFakes.Any && WebFakes.Find(Path(), out fake, out rest);
    }

    // The whole path from the window, a fake-kept object's included.
    internal IReadOnlyList<Step> Path()
    {
        var steps = new List<Step>();
        for (var c = this; c._kind != StepRoot; c = c._parent!)
        {
            steps.Add(new Step(c._kind, c._name!, c._args));
        }

        steps.Reverse();
        return Start._faked is { } kept ? [.. kept, .. steps] : steps;
    }

    // [root, steps, …the arguments the steps name by position].
    private object?[] Arguments()
    {
        var (steps, extras) = Serialize();
        return [Start._handle, steps, .. extras];
    }

    private JsChain Start
    {
        get
        {
            var chain = this;
            while (chain._parent is not null)
            {
                chain = chain._parent;
            }

            return chain;
        }
    }

    // The runtime of the object the chain started from, else the page handling the current event.
    private IJSRuntime Runtime =>
        Start._runtime
        ?? AmbientServices.Current?.GetService<IJSRuntime>()
        ?? throw new InvalidOperationException(
            "A web API was called outside a page: call it from an event handler or from OnRendered, where the page is live.");

    // [["g","navigator"],["g","clipboard"],["c","writeText",["hi"]]]
    internal string Steps() => Serialize().Steps;

    private (string Steps, List<object> Extras) Serialize()
    {
        var steps = new List<JsChain>();
        for (var c = this; c._kind != StepRoot; c = c._parent!)
        {
            steps.Add(c);
        }

        steps.Reverse();
        var extras = new List<object>();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var step in steps)
            {
                step.WriteStep(writer, extras);
            }

            writer.WriteEndArray();
        }

        return (Encoding.UTF8.GetString(buffer.WrittenSpan), extras);
    }

    private void WriteStep(Utf8JsonWriter writer, List<object> extras)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(_kind.ToString());
        writer.WriteStringValue(_name);
        if (_args is not null)
        {
            writer.WriteStartArray();
            foreach (var arg in _args)
            {
                WriteArg(writer, arg, extras);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void WriteArg(Utf8JsonWriter writer, object? arg, List<object> extras)
    {
        switch (arg)
        {
            case null:
                writer.WriteNullValue();
                break;
            case ScopedScript.ScriptCallback or IJSObjectReference or ElementRef:
                Placeholder(writer, arg, extras);
                break;
            case JsObject { Chain.IsKept: true } kept:
                Placeholder(writer, kept.Chain._handle!, extras);
                break;
            case JsObject:
                throw new InvalidOperationException("Only a kept object can be handed to the browser: await it first, to keep it.");
            default:
                JsonSerializer.Serialize(writer, arg, RaskWebJsonContext.Default.GetTypeInfo(arg.GetType())
                    ?? throw new NotSupportedException($"{arg.GetType()} has no JSON metadata in Rask.Web."));
                break;
        }
    }

    private static void Placeholder(Utf8JsonWriter writer, object value, List<object> extras)
    {
        writer.WriteStartObject();
        writer.WriteNumber("__raskArg__", extras.Count);
        writer.WriteEndObject();
        extras.Add(value);
    }

    private static JsonTypeInfo<T> TypeInfo<[DynamicallyAccessedMembers(Json)] T>() =>
        (JsonTypeInfo<T>)(RaskWebJsonContext.Default.GetTypeInfo(typeof(T))
                          ?? throw new NotSupportedException($"{typeof(T)} has no JSON metadata in Rask.Web."));

    // A subscription to a fake: disposing of it stops the fake's Raise reaching the handler.
    private sealed class FakeListening(WebFakes.Entry fake, WebFakes.Listener listener) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            fake.Listeners.Remove(listener);
            return default;
        }
    }

    // A subscription: disposing of it removes the listener in the browser and drops the handler here.
    private sealed class Listening(IJSRuntime runtime, int id, ScopedScript.ScriptCallback callback) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ScopedScript.Release(callback);
            return runtime.InvokeVoidAsync("__raskWeb.unlisten", id);
        }
    }
}
