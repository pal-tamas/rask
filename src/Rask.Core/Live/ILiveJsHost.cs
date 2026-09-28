namespace Rask.Core.Live;

// Implemented by both LiveSession (Server) and WasmLiveSession (WASM) so RaskJSRuntimeBase can queue
// a call and request a render without knowing the transport.
internal interface ILiveJsHost
{
    LiveJsInvokeQueue JsInvokes { get; }

    Task RequestRenderAsync();
}
