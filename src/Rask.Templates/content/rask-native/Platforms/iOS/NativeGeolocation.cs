using CoreLocation;
using Foundation;
using Rask.Core.Browser;
using UIKit;

namespace Company.RaskNative;

// Native iOS backend for IGeolocation — CoreLocation instead of the WebView's navigator.geolocation.
// Registered in AppDelegate before RunLocalAsync (overrides Rask's JS-backed default). Native location
// gives the real iOS permission prompt (Info.plist NSLocationWhenInUseUsageDescription) and CLLocationManager
// accuracy, and works even where the WebView's geolocation is restricted.
//
// This is the same framework-default → native-head-override pattern as NativeShare, for a request/response
// (+ subscription) capability: GetCurrentPositionAsync resolves one fix; WatchAsync streams them.
public sealed class NativeGeolocation : IGeolocation
{
    public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
    {
        var tcs = new TaskCompletionSource<GeolocationPosition>();
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            // The delegate keeps the manager alive until it fires (or faults), then tears it down.
            var manager = new CLLocationManager
            {
                DesiredAccuracy = (options?.EnableHighAccuracy ?? false)
                    ? CLLocation.AccuracyBest
                    : CLLocation.AccuracyHundredMeters
            };
            manager.Delegate = new OneShotDelegate(manager, tcs);
            manager.RequestWhenInUseAuthorization();
            RequestWhenAuthorized(manager);
        });
        return new ValueTask<GeolocationPosition>(tcs.Task);
    }

    public ValueTask<IAsyncDisposable> WatchAsync(
        Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
    {
        var watch = new Watch(onPosition, options?.EnableHighAccuracy ?? false);
        return new ValueTask<IAsyncDisposable>(watch);
    }

    // RequestLocation only works once authorization is at least WhenInUse; if it's still NotDetermined the
    // delegate re-requests from DidChangeAuthorization once the user answers the prompt.
    private static void RequestWhenAuthorized(CLLocationManager manager)
    {
        if (manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
            or CLAuthorizationStatus.AuthorizedAlways)
        {
            manager.RequestLocation();
        }
    }

    private sealed class OneShotDelegate(CLLocationManager manager, TaskCompletionSource<GeolocationPosition> tcs)
        : CLLocationManagerDelegate
    {
        public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
        {
            if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
            {
                mgr.RequestLocation();
            }
            else if (status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted)
            {
                Finish(() => tcs.TrySetException(new InvalidOperationException("Location permission denied.")));
            }
        }

        public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
        {
            if (locations.Length > 0)
            {
                Finish(() => tcs.TrySetResult(Map(locations[^1])));
            }
        }

        public override void Failed(CLLocationManager mgr, NSError error) =>
            Finish(() => tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription)));

        private void Finish(Action complete)
        {
            manager.Delegate = null;   // release the retain cycle so the manager can be collected
            complete();
        }
    }

    // A watchPosition subscription: streams every CLLocation update until disposed.
    private sealed class Watch : CLLocationManagerDelegate, IAsyncDisposable
    {
        private readonly Func<GeolocationPosition, Task> _onPosition;
        private CLLocationManager? _manager;

        public Watch(Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
        {
            _onPosition = onPosition;
            UIApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                _manager = new CLLocationManager
                {
                    DesiredAccuracy = highAccuracy ? CLLocation.AccuracyBest : CLLocation.AccuracyHundredMeters,
                    Delegate = this
                };
                _manager.RequestWhenInUseAuthorization();
                if (_manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
                    or CLAuthorizationStatus.AuthorizedAlways)
                {
                    _manager.StartUpdatingLocation();
                }
            });
        }

        public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
        {
            if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
            {
                mgr.StartUpdatingLocation();
            }
        }

        public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
        {
            if (locations.Length > 0)
            {
                _ = _onPosition(Map(locations[^1]));
            }
        }

        public ValueTask DisposeAsync()
        {
            UIApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                _manager?.StopUpdatingLocation();
                if (_manager is not null)
                {
                    _manager.Delegate = null;
                }
            });
            return default;
        }
    }

    private static GeolocationPosition Map(CLLocation l) => new(
        Latitude: l.Coordinate.Latitude,
        Longitude: l.Coordinate.Longitude,
        Accuracy: l.HorizontalAccuracy,
        Altitude: l.VerticalAccuracy > 0 ? l.EllipsoidalAltitude : null,
        AltitudeAccuracy: l.VerticalAccuracy > 0 ? l.VerticalAccuracy : null,
        Heading: l.Course >= 0 ? l.Course : null,
        Speed: l.Speed >= 0 ? l.Speed : null,
        TimestampMs: (l.Timestamp.SecondsSince1970) * 1000.0);
}
