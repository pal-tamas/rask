using System.Diagnostics;
using System.Reflection;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A handler or async lifecycle hook that throws stops an attached debugger, even though Rask catches
///     the exception and routes it to an error boundary.
/// </summary>
/// <remarks>
///     <para>
///         Without this the debugger sees a HANDLED exception and never stops: F5 shows the dev error panel
///         and leaves the developer to go and find the line. The fix is the standard .NET pair —
///         <see cref="DebuggerDisableUserUnhandledExceptionsAttribute" /> on the method whose catch swallows
///         the fault, and <see cref="Debugger.BreakForUserUnhandledException" /> inside that catch.
///     </para>
///     <para>
///         What a unit test can hold is the wiring, not the stop itself — that needs a debugger attached,
///         which is the by-hand check in the VS Code guide. Both halves are pinned because either alone does
///         nothing: the attribute on a method that never calls the API, or the call in a method the debugger
///         still treats as the user handling it.
///     </para>
/// </remarks>
public sealed class DebuggerBreakOnFaultTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    [Fact]
    public void The_handler_dispatch_is_not_the_user_handling_the_exception()
    {
        var dispatch = typeof(Component)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "TryInvokeHandlerAsync" && m.GetParameters().Length == 4);

        Assert.NotNull(dispatch.GetCustomAttribute<DebuggerDisableUserUnhandledExceptionsAttribute>());
    }

    [Fact]
    public void A_faulted_lifecycle_hook_is_not_the_user_handling_the_exception()
    {
        var report = typeof(Component).GetMethod("ReportLifecycleFault", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(report);
        Assert.NotNull(report!.GetCustomAttribute<DebuggerDisableUserUnhandledExceptionsAttribute>());
    }

    [Fact]
    public void Both_ask_the_debugger_to_stop_before_the_boundary_takes_the_fault()
    {
        var source = File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Component.cs"));

        AssertBreaksBefore(source, "Debugger.BreakForUserUnhandledException(ex);", "Trip(ex, ErrorSource.Action)");
        AssertBreaksBefore(source, "Debugger.BreakForUserUnhandledException(actual);", "Trip(actual, ErrorSource.Lifecycle)");
    }

    private static void AssertBreaksBefore(string source, string breakCall, string trip)
    {
        var tripAt = source.IndexOf(trip, StringComparison.Ordinal);
        Assert.True(tripAt > 0, $"'{trip}' is gone from Component.cs");

        var window = source[Math.Max(0, tripAt - 400)..tripAt];
        Assert.Contains(breakCall, window, StringComparison.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Rask.slnx).");
    }
}
