using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    internal const char StepElement = 'e';

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

    // Set on a kept root only: the C# handlers it was made with (an observer's), which it holds until it is disposed of.
    private readonly ScopedScript.ScriptCallback[] _handlers;

    private JsChain(
        JsChain? parent, char kind, string? name, object?[]? args, IJSObjectReference? handle = null, IJSRuntime? runtime = null,
        IReadOnlyList<Step>? faked = null, ScopedScript.ScriptCallback[]? handlers = null)
    {
        _parent = parent;
        _kind = kind;
        _name = name;
        _args = args;
        _handle = handle;
        _runtime = runtime;
        _faked = faked;
        _handlers = handlers ?? [];
    }

    // One step of a path, as a fake matches it: `matchMedia("(min-width: 900px)")` is not `matchMedia("print")`.
    internal readonly record struct Step(char Kind, string Name, object?[]? Args)
    {
        public bool Matches(Step other) =>
            Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && (Args ?? []).SequenceEqual(other.Args ?? []);
    }

    internal static JsChain Window { get; } = new(null, StepRoot, null, null);

    // A path from an element ref's element (`_video.RequestPictureInPicture()`): the ref rides beside the steps, as an
    // element argument does, and runs in the browser its element was rendered in.
    internal static JsChain Element(ElementRef element) => new(Window, StepElement, "", [element]);

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

    // A value of IDL `any` read as the app's own type, which Rask.Web's metadata cannot know: the host's runtime reads
    // it, with the options it reads any InvokeAsync<T> with (reflection where the app can run it, its contexts in AOT).
    internal ValueTask<T> ReadAny<[DynamicallyAccessedMembers(Json)] T>(string name) => Get(name).RunAny<T>();

    internal ValueTask<T> CallAny<[DynamicallyAccessedMembers(Json)] T>(string name) => Invoke(name).RunAny<T>();

    internal ValueTask<T> CallAny<[DynamicallyAccessedMembers(Json)] T>(string name, object?[] args) => Invoke(name, args).RunAny<T>();

    // An argument of IDL `any`: the app's own value, written with the host's options and Rask.Web's bytes (WebOptions),
    // riding beside the steps for the host to revive what it holds (a kept object). Annotated as InvokeAsync<T> is, so
    // the trimmer keeps what that writes.
    internal static object? Any<[DynamicallyAccessedMembers(Json)] T>(T value) => value is null ? null : new AnyArg(value, typeof(T));

    internal sealed class AnyArg(object content, [DynamicallyAccessedMembers(Json)] Type type)
    {
        public object Content { get; } = content;

        [DynamicallyAccessedMembers(Json)]
        public Type Type { get; } = type;
    }

    // The host's options with Rask.Web's bytes first: what the app's own values are written and read with, so a byte[]
    // anywhere in one crosses as a Uint8Array, not as the host's byte-array handle, which no web API takes. One copy per
    // host's options.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> s_webOptions = new();

    private static JsonSerializerOptions WebOptions(IJSRuntime runtime) =>
        s_webOptions.GetValue((runtime as RaskJSRuntimeBase)?.SerializerOptions ?? Unhosted, static host =>
        {
            // A host that names no resolver (the server's) leaves it to JSInterop, which reads by reflection: so does this.
            var options = new JsonSerializerOptions(host) { TypeInfoResolver = host.TypeInfoResolver ?? Unhosted.TypeInfoResolver };
            options.Converters.Insert(0, new BytesJsonConverter());
            return options;
        });

    // Runs the chain and keeps each item of the array it ends at as an object of its own (each USB device, each file
    // handle), disposed of one by one. The array is kept while that happens, so each item is taken from the array the
    // browser answered, by index, rather than by running the chain again: a handle to it, which slots hold an item, and
    // a handle per item. An empty slot (a gamepad not connected) stays null.
    internal async ValueTask<T?[]> KeepEachOrNull<T>(Func<JsChain, T> wrap)
        where T : class
    {
        if (Faked(out var fake, out var rest))
        {
            return fake.Answer<T?[]>(rest) ?? [];
        }

        var array = await Keep().ConfigureAwait(false);
        try
        {
            var slots = await array.Runtime.InvokeAsync<JsonElement>("__raskWeb.slots", array.Arguments()).ConfigureAwait(false);
            var items = new T?[slots.GetArrayLength()];
            for (var i = 0; i < items.Length; i++)
            {
                if (slots[i].ValueKind == JsonValueKind.True)
                {
                    items[i] = wrap(await array.Get(i.ToString(CultureInfo.InvariantCulture)).Keep().ConfigureAwait(false));
                }
            }

            return items;
        }
        finally
        {
            // The array only: any handler the chain was made with may still be the items' to call.
            await array._handle!.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async ValueTask<T[]> KeepEach<T>(Func<JsChain, T> wrap)
        where T : class =>
        [.. (await KeepEachOrNull(wrap).ConfigureAwait(false)).OfType<T>()];

    // Runs the chain and keeps what it ends at, a handle to an object the browser holds until it is disposed of.
    internal async ValueTask<JsChain> Keep()
    {
        if (Faked(out _, out _))
        {
            return new JsChain(null, StepRoot, null, null, faked: Path(), handlers: Handlers());
        }

        var runtime = Runtime;
        var handle = await runtime.InvokeAsync<IJSObjectReference>("__raskWeb.run", Arguments()).ConfigureAwait(false);
        return new JsChain(null, StepRoot, null, null, handle, runtime, handlers: Handlers());
    }

    internal async ValueTask<T> Keep<T>(Func<JsChain, T> wrap) => wrap(await Keep().ConfigureAwait(false));

    // The handlers the chain hands the browser: what the object it makes calls back, and is released with it.
    private ScopedScript.ScriptCallback[] Handlers()
    {
        var handlers = new List<ScopedScript.ScriptCallback>();
        for (var c = this; c._kind != StepRoot; c = c._parent!)
        {
            handlers.AddRange((c._args ?? []).OfType<ScopedScript.ScriptCallback>());
        }

        return [.. handlers];
    }

    // Whether the browser has what the chain ends at.
    internal ValueTask<bool> Exists() =>
        _kind == StepRoot || Faked(out _, out _) ? ValueTask.FromResult(true) : Runtime.InvokeAsync<bool>("__raskWeb.has", Arguments());

    // Lets the browser drop a kept object, and the handlers it was made with stop reaching C#.
    internal ValueTask Release()
    {
        foreach (var handler in _handlers)
        {
            ScopedScript.Release(handler);
        }

        return _handle?.DisposeAsync() ?? default;
    }

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

        var runtime = Runtime;
        var reading = new Reading(WebOptions(runtime), runtime, Owner(handler));
        var callback = ScopedScript.Handler(reading.Component, args => invoke(ReadEvent(read, First(args), reading)));
        var (steps, extras) = Serialize(reading.Options);
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

    // A C# handler as a function whose promise the browser waits on: a lock is held until it returns. It runs at once,
    // not queued behind the handler that is awaiting the call that runs it, which would wait on it forever.
    internal static object? Awaited(Action? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), _ => Done(handler), awaited: true);

    internal static object? Awaited(Func<Task>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), _ => handler(), awaited: true);

    internal static object? Awaited<[DynamicallyAccessedMembers(Json)] T1>(Action<T1>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => Done(() => handler(Arg<T1>(a, 0))), awaited: true);

    internal static object? Awaited<[DynamicallyAccessedMembers(Json)] T1>(Func<T1, Task>? handler) =>
        handler is null ? null : ScopedScript.Handler(Owner(handler), a => handler(Arg<T1>(a, 0)), awaited: true);

    // One field of an event's payload, as the generated payload types read it.
    internal static T Field<[DynamicallyAccessedMembers(Json)] T>(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) ? Value<T>(value) : default!;

    // A field of IDL `any` (a message's data), kept as its JSON until the handler reads it as its own type: e.Data<T>().
    internal static AnyField AnyFieldOf(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) ? new AnyField(value.Clone(), t_reading?.Options) : default;

    // A field holding a live object (a USB connection's device), which the listener kept for the handler: let go when it
    // is disposed of, or when the handler's component unmounts.
    internal static T KeptField<T>(JsonElement payload, string name, Func<JsChain, T> wrap)
        where T : class
    {
        if (t_reading is not { } reading || payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return null!;
        }

        var handle = value.Deserialize((JsonTypeInfo<IJSObjectReference>)reading.Options.GetTypeInfo(typeof(IJSObjectReference)))!;
        reading.Component.LifetimeTokenInternal.Register(static h => _ = ((IJSObjectReference)h!).DisposeAsync().AsTask(), handle);
        return wrap(new JsChain(null, StepRoot, null, null, handle, reading.JsRuntime));
    }

    // What an event's payload is read with, while it is: the options of the runtime it came over (its `any` fields are
    // read with them later), that runtime (its live fields' home), and the component whose handler it is.
    private sealed record Reading(JsonSerializerOptions Options, IJSRuntime JsRuntime, Component Component);

    [ThreadStatic] private static Reading? t_reading;

    // Reflection-backed web defaults, for a runtime that is not Rask's, or the resolver of one that names none (the
    // server's, which is never trimmed). The WebAssembly host passes its own source-generated options, so a trimmed app
    // never reaches this; a test's fake runtime does, where reflection is on.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reached only from a test's runtime, or the server's, which names no resolver and is never trimmed; the WebAssembly host passes its own.")]
    private static JsonSerializerOptions Unhosted => JsonSerializerOptions.Web;

    private static TEvent ReadEvent<TEvent>(Func<JsonElement, TEvent> read, JsonElement payload, Reading reading)
    {
        t_reading = reading;
        try
        {
            return read(payload);
        }
        finally
        {
            t_reading = null;
        }
    }

    internal readonly record struct AnyField(JsonElement Raw, JsonSerializerOptions? Options)
    {
        public T As<[DynamicallyAccessedMembers(Json)] T>() =>
            Options is null || Raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? default!
                : Raw.Deserialize((JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)))!;
    }

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

        var result = await Runtime.InvokeAsync<JsonElement>("__raskWeb.read", Arguments()).ConfigureAwait(false);
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

    private async ValueTask<T> RunAny<[DynamicallyAccessedMembers(Json)] T>()
    {
        if (Faked(out var fake, out var rest))
        {
            return fake.Answer<T>(rest);
        }

        var runtime = Runtime;
        var result = await runtime.InvokeAsync<JsonElement>("__raskWeb.read", Arguments()).ConfigureAwait(false);
        return new AnyField(result, WebOptions(runtime)).As<T>();
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
            steps.Add(new Step(c._kind, c._name!, c._args?.Select(a => a is AnyArg any ? any.Content : a).ToArray()));
        }

        steps.Reverse();
        return Start._faked is { } kept ? [.. kept, .. steps] : steps;
    }

    // [root, steps, …the arguments the steps name by position].
    private object?[] Arguments()
    {
        var (steps, extras) = Serialize(WebOptions(Runtime));
        return [Start._handle, steps, .. extras];
    }

    // The element ref a chain starts from, if it does: its first step.
    private ElementRef? FromElement
    {
        get
        {
            var chain = this;
            while (chain._parent is { _kind: not StepRoot } parent)
            {
                chain = parent;
            }

            return chain._kind == StepElement ? (ElementRef)chain._args![0]! : null;
        }
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

    // The runtime of the object the chain started from, or of the element, else the page handling the current event.
    private IJSRuntime Runtime =>
        Start._runtime
        ?? FromElement?.Runtime
        ?? AmbientServices.Current?.GetService<IJSRuntime>()
        ?? throw new InvalidOperationException(
            "A web API was called outside a page: call it from an event handler or from OnRendered, where the page is live.");

    // [["g","navigator"],["g","clipboard"],["c","writeText",["hi"]]]
    internal string Steps() => Serialize(null).Steps;

    // `options`: what the app's own values are written with (WebOptions); without them they ride as they are.
    private (string Steps, List<object> Extras) Serialize(JsonSerializerOptions? options)
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
                step.WriteStep(writer, extras, options);
            }

            writer.WriteEndArray();
        }

        return (Encoding.UTF8.GetString(buffer.WrittenSpan), extras);
    }

    private void WriteStep(Utf8JsonWriter writer, List<object> extras, JsonSerializerOptions? options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(_kind.ToString());
        writer.WriteStringValue(_name);
        if (_args is not null)
        {
            writer.WriteStartArray();
            foreach (var arg in _args)
            {
                WriteArg(writer, arg, extras, options);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void WriteArg(Utf8JsonWriter writer, object? arg, List<object> extras, JsonSerializerOptions? options)
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
            case AnyArg { Content: ScopedScript.ScriptCallback or IJSObjectReference or ElementRef or JsObject } any:
                WriteArg(writer, any.Content, extras, options);
                break;
            case AnyArg any:
                Placeholder(writer, options is null ? any.Content : JsonSerializer.SerializeToElement(any.Content, options.GetTypeInfo(any.Type)), extras, "__raskAny__");
                break;
            default:
                JsonSerializer.Serialize(writer, arg, RaskWebJsonContext.Default.GetTypeInfo(arg.GetType())
                    ?? throw new NotSupportedException($"{arg.GetType()} has no JSON metadata in Rask.Web."));
                break;
        }
    }

    // `kind`: __raskArg__ for what the host revives alone, __raskAny__ for the app's own value, whose bytes the browser
    // revives too.
    private static void Placeholder(Utf8JsonWriter writer, object value, List<object> extras, string kind = "__raskArg__")
    {
        writer.WriteStartObject();
        writer.WriteNumber(kind, extras.Count);
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
