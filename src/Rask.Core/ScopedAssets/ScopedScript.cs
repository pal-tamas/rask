using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     Infrastructure behind the private methods Rask generates for a component's scoped TypeScript:
///     <c>export function width(el: HTMLElement): number</c> in <c>Card.ts</c> becomes
///     <c>ValueTask&lt;double&gt; Width(ElementRef? el)</c> on <c>Card</c>. <b>Not for application use</b> —
///     call the generated method.
/// </summary>
/// <remarks>
///     The calls ride the host's own <see cref="IJSRuntime" />, found through the component's session, so
///     the component injects nothing. Everything a call hands to the browser that outlives it — a callback,
///     an object — is released when the component unmounts.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ScopedScript
{
    // What JSInterop itself asks of a result type, so a record the generator writes keeps its members
    // in a trimmed app.
    internal const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties;

    // RaskScopedCallback is a [JSInvokable] any socket can call, and on the Server host this registry is shared by
    // every live session — so each entry answers only the session that registered it (JsCallbacks, JsCaller).
    private static readonly JsCallbacks<Func<JsonElement, JsonSerializerOptions, Task?>> Callbacks = new();

    // No cancellation token on the calls: the component's lifetime token is cancelled on unmount, and a script's
    // teardown is exactly what OnUnmount and Dispose call — it has to go out after the token has fired.

    /// <summary>Calls an export that returns nothing.</summary>
    public static ValueTask Call(Component owner, string identifier, params object?[] args) =>
        Runtime(owner).InvokeVoidAsync(identifier, args);

    /// <summary>Calls an export and reads its result as <typeparamref name="T" />.</summary>
    public static ValueTask<T> Call<[DynamicallyAccessedMembers(JsonSerialized)] T>(
        Component owner, string identifier, params object?[] args) =>
        Runtime(owner).InvokeAsync<T>(identifier, args);

    /// <summary>
    ///     Calls an export that returns an instance of an exported class (a constructor included), and
    ///     wraps it in the generated proxy.
    /// </summary>
    public static async ValueTask<T> Object<T>(
        Component owner, string identifier, Func<IJSObjectReference, T> wrap, params object?[] args)
        where T : ScriptObject
    {
        var reference = await Runtime(owner)
            .InvokeAsync<IJSObjectReference>(identifier, args)
            .ConfigureAwait(false);
        return Adopt(owner, wrap(reference));
    }

    /// <summary>Hands a parameterless callback to the browser as a JS function.</summary>
    [DynamicDependency(nameof(Invoke), typeof(ScopedScript))]
    public static object Callback(Component owner, Callback callback) =>
        Register(owner, (_, _) => callback.Invoke());

    /// <summary>Hands a one-argument callback to the browser as a JS function.</summary>
    [DynamicDependency(nameof(Invoke), typeof(ScopedScript))]
    public static object Callback<T>(Component owner, Callback<T> callback) =>
        Register(owner, (args, options) => callback.Invoke(Arg<T>(args, 0, options)));

    /// <summary>Hands a two-argument callback to the browser as a JS function.</summary>
    [DynamicDependency(nameof(Invoke), typeof(ScopedScript))]
    public static object Callback<T1, T2>(Component owner, Callback<T1, T2> callback) =>
        Register(owner, (args, options) => callback.Invoke(Arg<T1>(args, 0, options), Arg<T2>(args, 1, options)));

    /// <summary>Infrastructure. Invoked by the browser when a script calls a callback it was handed; do not call.</summary>
    [JSInvokable("RaskScopedCallback")]
    public static Task Invoke(int id, JsonElement args)
    {
        if (!Callbacks.TryGet(id, out var callback))
        {
            // Released (the component unmounted while the script still held the function), or not this session's.
            return Task.CompletedTask;
        }

        return callback(args, Options) ?? Task.CompletedTask;
    }

    /// <summary>
    ///     Drops the unset optional arguments at the end of a call, so the script sees them as <c>undefined</c> — what
    ///     its default values and <c>=== undefined</c> checks test for — rather than <c>null</c>.
    /// </summary>
    public static object?[] Trim(object?[] args, int required)
    {
        var length = args.Length;
        while (length > required && args[length - 1] is null)
        {
            length--;
        }

        return length == args.Length ? args : args[..length];
    }

    internal static int CallbackCount => Callbacks.Count;

    internal static T Adopt<T>(Component owner, T value) where T : ScriptObject
    {
        value.Owner = owner;
        if (owner.IsTornDown)
        {
            // Created after its component left (an async handler finishing late): nothing would ever release it.
            _ = value.DisposeAsync();
            return value;
        }

        value.Release = owner.LifetimeTokenInternal.Register(
            static state => _ = ((ScriptObject)state!).DisposeAsync(), value);
        return value;
    }

    private static ScriptCallback Register(Component owner, Func<JsonElement, JsonSerializerOptions, Task?> invoke)
    {
        if (owner.IsTornDown)
        {
            // Nothing would release it, and nothing would run it: the component is gone. Id 0 is never issued.
            return new ScriptCallback(0);
        }

        var id = Callbacks.Register(TryRuntime(owner), (args, options) =>
        {
            owner.RunFromScript(() => invoke(args, options));
            return null;
        });
        owner.LifetimeTokenInternal.Register(static state => Callbacks.Unregister((int)state!), id);
        return new ScriptCallback(id);
    }

    private static T Arg<T>(JsonElement args, int index, JsonSerializerOptions options)
    {
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
        {
            return default!;
        }

        var info = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        return args[index].Deserialize(info)!;
    }

    // The runtime a callback's arguments are read with. Set by whichever host resolved one last — both
    // configure their runtime the same way (web defaults, plus the framework's source-generated metadata on
    // WASM), which is what the scripts wrote the arguments for.
    private static JsonSerializerOptions Options { get; set; } = ScopedScriptJsonContext.Default.Options;

    private static IJSRuntime? TryRuntime(Component owner) =>
        ((owner.RenderHandle as LiveSessionBase)?.Services ?? AmbientServices.Current)?.GetService<IJSRuntime>();

    private static IJSRuntime Runtime(Component owner)
    {
        var runtime = TryRuntime(owner)
            ?? throw new InvalidOperationException(
                $"{owner.GetType().Name} called its scoped TypeScript before it was on a page. Call it from an event "
                + "handler or from OnRendered, once the component is live.");
        if (runtime is RaskJSRuntimeBase rask)
        {
            Options = rask.SerializerOptions;
        }

        return runtime;
    }

    /// <summary>A C# callback on its way to the browser, which revives <c>{"__raskCb__": id}</c> into a function.</summary>
    [JsonConverter(typeof(ScriptCallbackConverter))]
    internal sealed class ScriptCallback(int id)
    {
        public int Id { get; } = id;
    }

    internal sealed class ScriptCallbackConverter : JsonConverter<ScriptCallback>
    {
        public override ScriptCallback Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("A scoped-script callback only travels from C# to the browser.");

        public override void Write(Utf8JsonWriter writer, ScriptCallback value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("__raskCb__", value.Id);
            writer.WriteEndObject();
        }
    }
}

/// <summary>
///     Infrastructure. The base of the proxy Rask generates for a class a component's scoped TypeScript
///     exports; each exported method becomes a typed method on the proxy. Disposed with its component.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ScriptObject : IAsyncDisposable
{
    private int _disposed;

    /// <summary>Wraps the browser-side instance.</summary>
    protected ScriptObject(IJSObjectReference reference) => Reference = reference;

    /// <summary>The browser-side instance — what an export taking this class is handed.</summary>
    public IJSObjectReference Reference { get; }

    internal Component? Owner { get; set; }

    internal CancellationTokenRegistration Release { get; set; }

    /// <summary>Releases the browser-side instance. Runs by itself when the owning component unmounts.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Release.Dispose();
        try
        {
            await Reference.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the instance with it.
        }
        catch (JSException)
        {
        }

        GC.SuppressFinalize(this);
    }

    // Named for the script rather than plainly (`Call`), because the generated proxy's own methods are
    // the class's method names, PascalCased — a TS `call()` must not collide with the machinery.

    /// <summary>Calls a method that returns nothing.</summary>
    protected ValueTask CallScript(string method, params object?[] args) => Reference.InvokeVoidAsync(method, args);

    /// <summary>Calls a method and reads its result as <typeparamref name="T" />.</summary>
    protected ValueTask<T> CallScript<[DynamicallyAccessedMembers(ScopedScript.JsonSerialized)] T>(
        string method, params object?[] args) => Reference.InvokeAsync<T>(method, args);

    /// <summary>Calls a method that returns another exported class's instance.</summary>
    protected async ValueTask<T> CallScriptObject<T>(
        string method, Func<IJSObjectReference, T> wrap, params object?[] args)
        where T : ScriptObject
    {
        var reference = await Reference.InvokeAsync<IJSObjectReference>(method, args).ConfigureAwait(false);
        var value = wrap(reference);
        return Owner is { } owner ? ScopedScript.Adopt(owner, value) : value;
    }

    /// <summary>Hands a parameterless callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback(Callback callback) => ScopedScript.Callback(RequireOwner(), callback);

    /// <summary>Hands a one-argument callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback<T>(Callback<T> callback) => ScopedScript.Callback(RequireOwner(), callback);

    /// <summary>Hands a two-argument callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback<T1, T2>(Callback<T1, T2> callback) =>
        ScopedScript.Callback(RequireOwner(), callback);

    private Component RequireOwner() =>
        Owner ?? throw new InvalidOperationException(
            $"{GetType().Name} was not created by its component, so it has no component to run a callback on.");
}

/// <summary>
///     Trim-safe metadata for what a generated scoped-script call puts on the wire by itself: the
///     primitives a TypeScript signature maps to, a callback's id and arguments, and <c>any</c>.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ScopedScript.ScriptCallback))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(IReadOnlyList<double>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class ScopedScriptJsonContext : JsonSerializerContext;
