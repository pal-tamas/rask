using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Live;
using Rask.Web.Types;

namespace Rask.Web;

/// <summary>
///     A path through the browser's objects — property reads and method calls from the window, or from an object a chain
///     kept — that the generated members of Rask.Web build and run in one round trip when awaited.
/// </summary>
/// <remarks>
///     The steps cross as one JSON string, written with Rask.Web's own trim-safe metadata, so neither host's runtime has
///     to know Rask.Web's types: the runtime carries a string there and a <see cref="JsonElement" /> back.
/// </remarks>
internal sealed class JsChain
{
    private const char StepRoot = '\0';
    private const char StepRead = 'g';
    private const char StepCall = 'c';
    private const char StepWrite = 's';
    private const char StepNew = 'n';

    private readonly JsChain? _parent;
    private readonly char _kind;
    private readonly string? _name;
    private readonly object?[]? _args;

    // Set on a root only: a kept object and the runtime it lives in.
    private readonly IJSObjectReference? _handle;
    private readonly IJSRuntime? _runtime;

    private JsChain(JsChain? parent, char kind, string? name, object?[]? args, IJSObjectReference? handle = null, IJSRuntime? runtime = null)
    {
        _parent = parent;
        _kind = kind;
        _name = name;
        _args = args;
        _handle = handle;
        _runtime = runtime;
    }

    /// <summary>The window: where every global starts.</summary>
    internal static JsChain Window { get; } = new(null, StepRoot, null, null);

    // A chain that is an object the browser holds for us rather than a path to one.
    internal bool IsKept => _kind == StepRoot && _handle is not null;

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
        var runtime = Runtime;
        var handle = await runtime.InvokeAsync<IJSObjectReference>("__raskWeb.run", Start._handle, Steps()).ConfigureAwait(false);
        return new JsChain(null, StepRoot, null, null, handle, runtime);
    }

    internal async ValueTask<T> Keep<T>(Func<JsChain, T> wrap) => wrap(await Keep().ConfigureAwait(false));

    // Whether the browser has what the chain ends at.
    internal ValueTask<bool> Exists() =>
        _kind == StepRoot ? ValueTask.FromResult(true) : Runtime.InvokeAsync<bool>("__raskWeb.has", Start._handle, Steps());

    internal ValueTask Release() => _handle?.DisposeAsync() ?? default;

    private async ValueTask<T> Run<[DynamicallyAccessedMembers(Json)] T>()
    {
        var result = await Runtime.InvokeAsync<JsonElement>("__raskWeb.run", Start._handle, Steps()).ConfigureAwait(false);
        return result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? default! : result.Deserialize(TypeInfo<T>())!;
    }

    private ValueTask Run() => Runtime.InvokeVoidAsync("__raskWeb.run", Start._handle, Steps());

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
    internal string Steps()
    {
        var steps = new List<JsChain>();
        for (var c = this; c._kind != StepRoot; c = c._parent!)
        {
            steps.Add(c);
        }

        steps.Reverse();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var step in steps)
            {
                step.WriteStep(writer);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteStep(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(_kind.ToString());
        writer.WriteStringValue(_name);
        if (_args is not null)
        {
            writer.WriteStartArray();
            foreach (var arg in _args)
            {
                if (arg is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    JsonSerializer.Serialize(writer, arg, RaskWebJsonContext.Default.GetTypeInfo(arg.GetType())
                        ?? throw new NotSupportedException($"{arg.GetType()} has no JSON metadata in Rask.Web."));
                }
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static JsonTypeInfo<T> TypeInfo<[DynamicallyAccessedMembers(Json)] T>() =>
        (JsonTypeInfo<T>)(RaskWebJsonContext.Default.GetTypeInfo(typeof(T))
                          ?? throw new NotSupportedException($"{typeof(T)} has no JSON metadata in Rask.Web."));

    // What JSInterop's InvokeAsync<T> asks of a result type, so the trimmer keeps it deserializable.
    private const DynamicallyAccessedMemberTypes Json =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;
}
