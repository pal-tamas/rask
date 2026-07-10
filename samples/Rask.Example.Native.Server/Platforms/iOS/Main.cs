using UIKit;

namespace Rask.Example.Native.Server;

// iOS entry point. Hands control to UIKit with our AppDelegate (Local) or the Server-mode AppDelegate
// (ServerAppDelegate.cs also declares the class AppDelegate; the RaskNativeHost gate compiles exactly one).
public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
