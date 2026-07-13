namespace Rask.Native.Appium.Tests;

// Configuration for the Appium on-device E2E, read from the environment so the same tests run in the
// macOS `native-appium` CI job and locally. Every test SKIPS unless RASK_APPIUM_SERVER is set.
internal static class AppiumEnv
{
    public const string SkipReason =
        "Appium on-device E2E: set RASK_APPIUM_SERVER (+ RASK_APPIUM_ANDROID_APP / RASK_APPIUM_IOS_APP) " +
        "with a booted emulator/simulator and a running Appium server to run these.";

    /// <summary>The Appium server endpoint, e.g. http://127.0.0.1:4723. Absent → the tests skip.</summary>
    public static string? ServerUrl => Get("RASK_APPIUM_SERVER");

    public static bool Enabled => !string.IsNullOrEmpty(ServerUrl);

    /// <summary>Path to the built + signed Android .apk. Appium installs it and launches its main activity.</summary>
    public static string? AndroidApp => Get("RASK_APPIUM_ANDROID_APP");

    /// <summary>Path to the built iOS .app (simulator). Appium installs + launches it.</summary>
    public static string? IosApp => Get("RASK_APPIUM_IOS_APP");

    /// <summary>The booted iOS simulator name, e.g. "iPhone 17 Pro".</summary>
    public static string IosDeviceName => Get("RASK_APPIUM_IOS_DEVICE") ?? "iPhone 17";

    /// <summary>
    ///     The booted iOS simulator's UDID. When present it is passed as <c>appium:udid</c> for EXPLICIT
    ///     device targeting — on ARM64 macOS runners (every GitHub macOS runner) Appium/XCUITest device
    ///     selection by name alone is unreliable, so the CI job resolves the UDID and pins it (the same
    ///     reason .NET MAUI's XHarness passes an explicit <c>--device UDID</c>).
    /// </summary>
    public static string? IosUdid => Get("RASK_APPIUM_IOS_UDID");

    private static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
