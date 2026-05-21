using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace Rask.Wasm;

/// <summary>
///     <see cref="JSRuntime" /> implementation backed by the WASM <c>[JSImport]</c> /
///     <c>[JSExport]</c> bridge. Mirrors <c>Rask.Server.JSInterop.RaskJSRuntime</c>'s
///     contract: every <c>IJSRuntime.InvokeAsync</c> call lands in
///     <see cref="BeginInvokeJS(long, string, string?, JSCallResultType, long)" />, which
///     hands the call to <c>rask.wasm.js</c>'s <c>dispatchJsInvoke</c>. Results return
///     through the <c>endInvokeJSResult</c> <c>[JSExport]</c> in
///     <see cref="JSInterop" /> (which calls <see cref="DotNetDispatcher.EndInvokeJS" />).
///     <para>
///         Trim safety: same caveat as <c>RaskJSRuntime</c> — base-class
///         <c>JsonSerializer.Deserialize&lt;TValue&gt;</c> isn't trim-safe. Users calling
///         <c>InvokeAsync&lt;ComplexType&gt;</c> on WASM must keep the type rooted (via
///         DAM on the call site or a <c>JsonSerializerContext</c>). Mirrors Blazor WASM.
///     </para>
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Forwards TValue's trim annotations from IJSRuntime.InvokeAsync<TValue>. " +
                    "Users must keep their TValue types rooted on WASM.")]
internal sealed class WasmJSRuntime : JSRuntime
{
    public WasmJSRuntime()
    {
        // The base JSRuntime's JsonSerializerOptions ships with no TypeInfoResolver,
        // so Serialize / Deserialize<T> falls back to the runtime default. PublishTrimmed
        // apps (this includes Rask.Example.Wasm) flip
        // JsonSerializer.IsReflectionEnabledByDefault to false, and that fallback then
        // throws "JsonSerializerIsReflectionDisabled" on the very first
        // InvokeAsync<string> — including the built-in primitive case. Explicitly
        // chaining the reflection-based resolver here makes InvokeAsync<T> work for
        // any T the user (or framework) can keep rooted via DAM or a
        // JsonSerializerContext. Same model Blazor WASM ships with.
        JsonSerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    }

    protected override void BeginInvokeJS(
        long taskId,
        string identifier,
        string? argsJson,
        JSCallResultType resultType,
        long targetInstanceId)
    {
        // Pass through to the JS-side dispatcher. taskId / targetInstanceId travel as
        // strings to avoid BigInt marshalling on the JS boundary — they're rebuilt as
        // numbers inside the dispatcher (safe for the values we mint).
        JSInterop.BeginInvokeJSImport(
            taskId.ToString(),
            identifier,
            argsJson,
            (int)resultType,
            targetInstanceId.ToString());
    }

    protected override void EndInvokeDotNet(
        DotNetInvocationInfo invocationInfo,
        in DotNetInvocationResult invocationResult)
    {
        // Ship a [JSInvokable] call's result back to the JS-side `DotNet` shim. Mirrors
        // RaskJSRuntime's wire shape (`type: "dotNetResult"`), encoded as JSON so the
        // single JSImport signature stays type-stable.
        var payload = BuildDotNetResultJson(
            invocationInfo.CallId,
            invocationResult.Success,
            invocationResult.Success ? invocationResult.ResultJson : null,
            invocationResult.Success
                ? null
                : invocationResult.Exception?.Message ?? "DotNet invocation failed");
        JSInterop.EndDotNetInvokeImport(payload);
    }

    private static string BuildDotNetResultJson(string? callId, bool success, string? resultJson, string? error)
    {
        using var stream = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (callId is not null)
            {
                writer.WriteString("callId", callId);
            }

            writer.WriteBoolean("success", success);
            if (resultJson is not null)
            {
                writer.WritePropertyName("result");
                using var doc = JsonDocument.Parse(resultJson);
                doc.RootElement.WriteTo(writer);
            }

            if (error is not null)
            {
                writer.WriteString("error", error);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
