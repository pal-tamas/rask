using UIKit;

namespace Company.RaskNative;

// iOS entry point. Hands control to UIKit with our AppDelegate.
public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
