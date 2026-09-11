using System.Diagnostics;
using System.Reflection;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A handler or async lifecycle hook that throws stops an attached debugger once, even though Rask catches
///     the exception and routes it to an error boundary.
/// </summary>
/// <remarks>
///     <para>
///         Measured under VS Code's F5, not assumed. A click handler's exception leaves the user's code for
///         Rask's, and a debugger with Just My Code reports that as user-unhandled on its own — it stops on the
///         throw line. An explicit <see cref="Debugger.BreakForUserUnhandledException" /> in that catch made it
///         stop a second time at the same line, so the handler path deliberately has none.
///     </para>
///     <para>
///         What a unit test can hold is the wiring, not the stop itself — that needs a debugger attached, which
///         is the by-hand check in the VS Code guide.
///     </para>
/// </remarks>
public sealed class DebuggerBreakOnFaultTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ComponentSource =>
        File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Component.cs"));

    [Fact]
    public void The_handler_dispatch_leaves_the_stop_to_the_debugger()
    {
        // The debugger already stops on the throw line; a break here is the second, redundant stop.
        var dispatch = typeof(Component)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "TryInvokeHandlerAsync" && m.GetParameters().Length == 4);

        Assert.Null(dispatch.GetCustomAttribute<DebuggerDisableUserUnhandledExceptionsAttribute>());
        Assert.DoesNotContain(
            "BreakForUserUnhandledException(ex)",
            Before(ComponentSource, "Trip(ex, ErrorSource.Action)"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_faulted_lifecycle_hook_is_not_the_user_handling_the_exception()
    {
        var report = typeof(Component).GetMethod("ReportLifecycleFault", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(report);
        Assert.NotNull(report!.GetCustomAttribute<DebuggerDisableUserUnhandledExceptionsAttribute>());
    }

    [Fact]
    public void A_faulted_lifecycle_hook_asks_the_debugger_to_stop_before_the_boundary_takes_it()
    {
        // The opposite of the handler path, and measured the same way: an async hook's exception arrives through
        // its faulted task, the debugger does not stop for it on its own, and this call was the only stop it made.
        Assert.Contains(
            "Debugger.BreakForUserUnhandledException(actual);",
            Before(ComponentSource, "Trip(actual, ErrorSource.Lifecycle)"),
            StringComparison.Ordinal);
    }

    /// <summary>The 400 characters of <paramref name="source" /> before <paramref name="marker" />.</summary>
    private static string Before(string source, string marker)
    {
        var at = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at > 0, $"'{marker}' is gone from Component.cs");

        return source[Math.Max(0, at - 400)..at];
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
