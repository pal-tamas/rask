namespace Rask.Printing.Tests;

public sealed class ChromiumStartFailureTests
{
    [Fact]
    public void A_missing_library_is_named_with_the_ubuntu_package_that_ships_it()
    {
        string[] log = ["/opt/rask/chrome/chrome-headless-shell: error while loading shared libraries: libatk-bridge-2.0.so.0: cannot open shared object file: No such file or directory"];

        var cause = ChromiumStartFailure.Explain(log);

        Assert.Equal(
            "Chromium cannot start: libatk-bridge-2.0.so.0 is missing. Install it in the image: apt-get install libatk-bridge2.0-0t64 (Ubuntu 24.04 and later).",
            cause);
    }

    [Fact]
    public void A_missing_library_outside_the_map_asks_for_whatever_package_provides_it()
    {
        string[] log = ["chrome: error while loading shared libraries: libfoo.so.7: cannot open shared object file"];

        var cause = ChromiumStartFailure.Explain(log);

        Assert.Equal("Chromium cannot start: libfoo.so.7 is missing. Install the package that provides libfoo.so.7.", cause);
    }

    [Fact]
    public void A_sandbox_the_host_forbids_is_named_with_the_setting_that_turns_it_off()
    {
        string[] log =
        [
            "[1022/093900.949166:FATAL:content/browser/zygote_host/zygote_host_impl_linux.cc:130] No usable sandbox! If you are running on Ubuntu 23.10+ …",
        ];

        var cause = ChromiumStartFailure.Explain(log);

        Assert.NotNull(cause);
        Assert.Contains("Rask:Pdf:NoSandbox=true (Rask__Pdf__NoSandbox)", cause, StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_that_names_no_known_cause_explains_nothing()
    {
        string[] log = ["[1022/093739.641389:WARNING:media/gpu/vaapi/vaapi_wrapper.cc:1660] drmGetDevices2() has not found any devices"];

        var cause = ChromiumStartFailure.Explain(log);

        Assert.Null(cause);
    }

    [Fact]
    public async Task A_browser_that_dies_for_a_missing_library_fails_the_print_with_its_package()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Rask.Pdf starts Chromium on Linux and macOS only.");
            return;
        }

        var bin = Directory.CreateTempSubdirectory("rask-pdf-dead-");
        var chromium = Path.Combine(bin.FullName, "chrome-headless-shell");
        File.WriteAllText(
            chromium,
            "#!/bin/sh\necho \"$0: error while loading shared libraries: libnss3.so: cannot open shared object file\" >&2\nexit 127\n");
        File.SetUnixFileMode(chromium, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);
        await using var printer = new ChromiumPrinter(new ChromiumSettings { ChromiumPath = chromium });

        var failed = await Assert.ThrowsAsync<IOException>(
            () => printer.Print(PrintDocument.Of("<p>x</p>"), TestContext.Current.CancellationToken));

        Assert.Contains("apt-get install libnss3 ", failed.Message, StringComparison.Ordinal);
        bin.Delete(recursive: true);
    }
}
