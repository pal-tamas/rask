using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Native;

internal static partial class NativeCapabilityDispatch
{
    /// <summary>
    ///     The six members that push instead of returning: the geolocation and battery watches, the two
    ///     sensor streams, speech recognition, and the wake lock's held sentinel.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All six have the same shape — start, hold a handle, push until released — so they share one
    ///         path rather than six. The handle stays native-side; the page refers to it by id.
    ///     </para>
    ///     <para>
    ///         <b>The page mints that id</b>, rather than receiving it in the reply. A sensor can deliver a
    ///         reading before the reply reaches the page, and an id the page has not seen yet is one it
    ///         cannot route — the first readings would be dropped, silently and only on a fast device.
    ///         Minting it caller-side means the callback is registered before the request is even sent.
    ///     </para>
    ///     <para>
    ///         What does not change: readings still reach the app's C# through the page's own
    ///         <c>DotNet.invokeMethodAsync</c> path, exactly as the web implementation delivers them. The
    ///         bridge replaces where a reading comes from, not how it gets home.
    ///     </para>
    /// </remarks>
    private static async ValueTask<string?> StreamAsync(
        IServiceProvider services, string component, string op, string? dataJson, Func<string, ValueTask> evaluate)
    {
        var subscriptions = services.GetService<NativeCapabilitySubscriptions>()
            ?? throw new NotSupportedException(
                "This head cannot hold capability subscriptions — none is registered on it.");

        // Every stream ends the same way, so one release path serves all of them.
        if (op is "unwatch" or "stop" or "release")
        {
            await subscriptions.ReleaseAsync(Text(dataJson)).ConfigureAwait(false);
            return null;
        }

        IAsyncDisposable handle;
        string sub;

        switch (component)
        {
            case "geolocation" when op == "watch":
            {
                var request = Parse(dataJson, NativeCapabilityJsonContext.Default.GeolocationWatchRequest)
                    ?? throw Bad("geolocation.watch", "a subscription id");
                sub = request.Sub;
                handle = await Required<IGeolocation>(services, "geolocation")
                    .WatchAsync(Push(sub, evaluate, NativeCapabilityJsonContext.Default.GeolocationPosition),
                        request.Options).ConfigureAwait(false);
                break;
            }

            case "speechRecognition" when op == "start":
            {
                var request = Parse(dataJson, NativeCapabilityJsonContext.Default.SpeechStartRequest)
                    ?? throw Bad("speechRecognition.start", "a subscription id");
                sub = request.Sub;
                handle = await Required<ISpeechRecognition>(services, "speechRecognition")
                    .StartAsync(Push(sub, evaluate, NativeCapabilityJsonContext.Default.RecognitionResult),
                        request.Options).ConfigureAwait(false);
                break;
            }

            case "battery" when op == "watch":
            {
                sub = SubIdOf(dataJson, "battery.watch");
                handle = await Required<IBattery>(services, "battery")
                    .WatchAsync(Push(sub, evaluate, NativeCapabilityJsonContext.Default.BatteryStatus))
                    .ConfigureAwait(false);
                break;
            }

            case "deviceOrientation" when op == "watch":
            {
                sub = SubIdOf(dataJson, "deviceOrientation.watch");
                handle = await Required<IDeviceOrientation>(services, "deviceOrientation")
                    .WatchAsync(Push(sub, evaluate, NativeCapabilityJsonContext.Default.OrientationReading))
                    .ConfigureAwait(false);
                break;
            }

            case "deviceMotion" when op == "watch":
            {
                sub = SubIdOf(dataJson, "deviceMotion.watch");
                handle = await Required<IDeviceMotion>(services, "deviceMotion")
                    .WatchAsync(Push(sub, evaluate, NativeCapabilityJsonContext.Default.MotionReading))
                    .ConfigureAwait(false);
                break;
            }

            case "wakeLock" when op == "request":
            {
                // A sentinel IS its handle — releasing it is DisposeAsync, the same contract a watch has, so
                // it rides the same path rather than needing one of its own.
                sub = SubIdOf(dataJson, "wakeLock.request");
                handle = await Required<IWakeLock>(services, "wakeLock").RequestAsync().ConfigureAwait(false);
                break;
            }

            default:
                throw Unknown(component, op);
        }

        subscriptions.Add(sub, handle);
        return null;
    }

    /// <summary>Which ops push rather than return, so the main switch can route them here.</summary>
    private static bool IsStreamOp(string component, string op) =>
        op is "unwatch" or "stop" or "release"
        || (component, op) is ("geolocation", "watch")
            or ("battery", "watch")
            or ("deviceOrientation", "watch")
            or ("deviceMotion", "watch")
            or ("speechRecognition", "start")
            or ("wakeLock", "request");

    private static string SubIdOf(string? dataJson, string what) =>
        (Parse(dataJson, NativeCapabilityJsonContext.Default.WatchRequest)
         ?? throw Bad(what, "a subscription id")).Sub;

    // One reading → one capabilityEvent. Serialized through the AOT context and encoded as a JS string
    // literal, for the same reason every other push at this boundary is: nothing here may need reflection.
    private static Func<T, Task> Push<T>(
        string sub,
        Func<string, ValueTask> evaluate,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        async reading =>
        {
            var payload = JsonSerializer.Serialize(
                new NativeCapabilityEvent(sub, JsonSerializer.Serialize(reading, type)),
                NativeCapabilityJsonContext.Default.NativeCapabilityEvent);

            await evaluate("window.__raskNative.capabilityEvent(\"" + JsonEncodedText.Encode(payload) + "\")")
                .ConfigureAwait(false);
        };
}
