using System.Runtime.InteropServices;

namespace KitBundle;

/// <summary>This machine, named the way npm's <c>os</c> and <c>cpu</c> fields name it.</summary>
internal static class Platform
{
    internal static string NpmOs { get; } =
        OperatingSystem.IsMacOS() ? "darwin" : OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsLinux() ? "linux" : "unknown";

    internal static string NpmCpu { get; } = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "ia32",
        Architecture.Arm => "arm",
        _ => "unknown",
    };
}
