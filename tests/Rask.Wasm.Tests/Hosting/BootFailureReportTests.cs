namespace Rask.Wasm.Tests.Hosting;

/// <summary>
///     The managed half of the boot-failure report (#817): what <see cref="WasmHostBuilder" /> does when
///     startup throws before there is a mounted tree for the root error boundary to render into.
/// </summary>
/// <remarks>
///     <para>
///         This exercises <c>ReportBootFailure</c> directly rather than driving a whole
///         <c>RunAsync&lt;TApp&gt;</c> with a throwing app. Booting for real installs a process-wide
///         diagnostics sink and rebinds the static JS interop bridge, and this assembly runs its classes in
///         parallel — the same shape as the two flakes already on record here, where a test's global sink
///         unhooked a live one in another class. The catch block itself is a <c>throw;</c> after this call,
///         so what is worth guarding is what this method does, and it can be reached with no global state
///         touched at all.
///     </para>
///     <para>
///         Reporting is best-effort by design and must never throw: it runs on the way to rethrowing the
///         original exception, and losing that one to a secondary failure inside the reporter would be
///         strictly worse than reporting nothing.
///     </para>
/// </remarks>
public sealed class BootFailureReportTests
{
    [Fact]
    public void A_boot_failure_is_reported_with_the_exception_attached()
    {
        JSInterop.ResetBootFailure();

        WasmHostBuilder.ReportBootFailure(new InvalidOperationException("no service for IThing"));

        var reported = JSInterop.LastBootFailure;
        Assert.NotNull(reported);
        Assert.Equal("The app failed to start.", reported!.Value.Message);
        // ToString(), not Message: the type and the stack are what turn "it failed" into something
        // actionable, and this is the only place they can still be recovered — by the time this reaches
        // JS through runMain it is an opaque rejected promise.
        Assert.Contains("InvalidOperationException", reported.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("no service for IThing", reported.Value.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The JS module import is the first thing boot does, so it is one of the things that can fail —
    ///     and then the <c>bootFailed</c> JSImport has no module to call into. That must degrade to
    ///     reporting nothing, not to replacing the real exception with a marshalling one.
    /// </summary>
    [Fact]
    public void Reporting_never_throws_even_when_there_is_no_JS_module_to_report_to()
    {
        JSInterop.ResetBootFailure();

        var ex = Record.Exception(() => WasmHostBuilder.ReportBootFailure(new TypeLoadException("boom")));

        Assert.Null(ex);
    }
}
