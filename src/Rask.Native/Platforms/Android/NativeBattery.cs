using Android.Content;
using Rask.Core.Browser;
using AndroidBatteryStatus = Android.OS.BatteryStatus;
using BatteryManager = Android.OS.BatteryManager;

namespace Rask.Native;

// Native Android backend for IBattery — the sticky ACTION_BATTERY_CHANGED intent / BatteryManager instead of
// navigator.getBattery (which the WebView doesn't implement). Registered by AndroidPlatform. Android exposes
// level + charging status but not a reliable charge/discharge time to apps, so those fields are null.
//
// `Android.OS` is imported only via aliases: its `BatteryStatus` enum would otherwise collide with the Rask
// `BatteryStatus` record (CS0104), and the `BatteryManager.BatteryStatus*` int fields are [Obsolete(error)] —
// the enum members are the supported form.
internal sealed class NativeBattery(Context context) : IBattery
{
    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<BatteryStatus?> GetStatusAsync()
    {
        using var filter = new IntentFilter(Intent.ActionBatteryChanged);
        // A null receiver returns the current sticky battery intent without registering anything.
        var intent = context.RegisterReceiver(null, filter);
        return new ValueTask<BatteryStatus?>(intent is null ? null : Read(intent));
    }

    public ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        return new ValueTask<IAsyncDisposable>(new Watch(context, onChange));
    }

    private static BatteryStatus Read(Intent intent)
    {
        var level = intent.GetIntExtra(BatteryManager.ExtraLevel, -1);
        var scale = intent.GetIntExtra(BatteryManager.ExtraScale, -1);
        var status = (AndroidBatteryStatus)intent.GetIntExtra(BatteryManager.ExtraStatus, -1);
        var fraction = level >= 0 && scale > 0 ? (double)level / scale : 0;
        var charging = status is AndroidBatteryStatus.Charging or AndroidBatteryStatus.Full;
        return new BatteryStatus(fraction, charging, null, null);
    }

    private sealed class Watch : BroadcastReceiver, IAsyncDisposable
    {
        private readonly Context _context;
        private readonly Func<BatteryStatus, Task> _onChange;

        public Watch(Context context, Func<BatteryStatus, Task> onChange)
        {
            _context = context;
            _onChange = onChange;
            _context.RegisterReceiver(this, new IntentFilter(Intent.ActionBatteryChanged));
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is not null)
            {
                _ = _onChange(Read(intent));
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _context.UnregisterReceiver(this);
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                // Already unregistered (e.g. double dispose) — nothing to do.
            }

            return default;
        }
    }
}
