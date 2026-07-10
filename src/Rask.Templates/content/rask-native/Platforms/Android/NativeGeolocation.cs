using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Rask.Core.Browser;

namespace Company.RaskNative;

// Native Android backend for IGeolocation — the platform LocationManager instead of the WebView's
// navigator.geolocation. Registered in MainActivity before RunLocalAsync (overrides Rask's JS-backed
// default). Needs ACCESS_FINE_LOCATION in AndroidManifest.xml + a runtime grant (MainActivity requests it
// on launch). Same framework-default → native-head-override pattern as NativeShare, for a request/response
// (+ subscription) capability.
public sealed class NativeGeolocation(Activity activity) : IGeolocation
{
    public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
    {
        var tcs = new TaskCompletionSource<GeolocationPosition>();
        activity.RunOnUiThread(() =>
        {
            if (!HasPermission())
            {
                tcs.TrySetException(new InvalidOperationException("Location permission not granted."));
                return;
            }

            var manager = LocationManagerFor();
            var provider = BestProvider(manager, options?.EnableHighAccuracy ?? false);
            if (provider is null)
            {
                tcs.TrySetException(new InvalidOperationException("No enabled location provider."));
                return;
            }

            // One-shot: subscribe, take the first fix, then unsubscribe.
            OneShotListener? listener = null;
            listener = new OneShotListener(fix =>
            {
                manager.RemoveUpdates(listener!);
                tcs.TrySetResult(fix);
            });
            manager.RequestLocationUpdates(provider, 0L, 0f, listener, Looper.MainLooper);
        });
        return new ValueTask<GeolocationPosition>(tcs.Task);
    }

    public ValueTask<IAsyncDisposable> WatchAsync(
        Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
    {
        var watch = new Watch(activity, HasPermission, onPosition, options?.EnableHighAccuracy ?? false);
        return new ValueTask<IAsyncDisposable>(watch);
    }

    private bool HasPermission() =>
        activity.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted;

    private LocationManager LocationManagerFor() =>
        (LocationManager)activity.GetSystemService(Context.LocationService)!;

    private static string? BestProvider(LocationManager manager, bool highAccuracy)
    {
        // Prefer GPS for high accuracy; otherwise take whatever's enabled (network is faster/cheaper).
        var order = highAccuracy
            ? new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider }
            : new[] { LocationManager.NetworkProvider, LocationManager.GpsProvider };
        foreach (var p in order)
        {
            if (manager.IsProviderEnabled(p))
            {
                return p;
            }
        }

        return null;
    }

    private static GeolocationPosition Map(Location l) => new(
        Latitude: l.Latitude,
        Longitude: l.Longitude,
        Accuracy: l.HasAccuracy ? l.Accuracy : 0,
        Altitude: l.HasAltitude ? l.Altitude : null,
        AltitudeAccuracy: l is { HasVerticalAccuracy: true } ? l.VerticalAccuracyMeters : null,
        Heading: l.HasBearing ? l.Bearing : null,
        Speed: l.HasSpeed ? l.Speed : null,
        TimestampMs: l.Time);

    private sealed class OneShotListener(Action<GeolocationPosition> onFix) : Java.Lang.Object, ILocationListener
    {
        public void OnLocationChanged(Location location) => onFix(Map(location));

        public void OnProviderDisabled(string provider) { }

        public void OnProviderEnabled(string provider) { }

        public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }
    }

    private sealed class Watch : Java.Lang.Object, ILocationListener, IAsyncDisposable
    {
        private readonly Activity _activity;
        private readonly Func<GeolocationPosition, Task> _onPosition;
        private LocationManager? _manager;

        public Watch(Activity activity, Func<bool> hasPermission, Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
        {
            _activity = activity;
            _onPosition = onPosition;
            activity.RunOnUiThread(() =>
            {
                if (!hasPermission())
                {
                    return;
                }

                _manager = (LocationManager)activity.GetSystemService(Context.LocationService)!;
                var provider = BestProvider(_manager, highAccuracy);
                if (provider is not null)
                {
                    _manager.RequestLocationUpdates(provider, 1000L, 0f, this, Looper.MainLooper);
                }
            });
        }

        public void OnLocationChanged(Location location) => _ = _onPosition(Map(location));

        public void OnProviderDisabled(string provider) { }

        public void OnProviderEnabled(string provider) { }

        public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }

        public ValueTask DisposeAsync()
        {
            _activity.RunOnUiThread(() => _manager?.RemoveUpdates(this));
            return default;
        }
    }
}
