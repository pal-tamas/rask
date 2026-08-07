using System.Text.Json;
using Rask.Cli.Dev;

namespace Rask.Cli.Tests;

/// <summary>
///     The half of #603 that has to be exactly right, and the half that needs no compiler, no file watcher
///     and no browser to check: a pure state machine over <c>dotnet watch</c>'s output.
/// </summary>
public sealed class DevBuildWatcherTests
{
    private const string Error = "/app/Pages/Home.cs(12,9): error CS0103: The name 'x' does not exist in the current context [/app/App.csproj]";
    private const string OtherError = "/app/Pages/Home.cs(14,9): error CS1002: ; expected [/app/App.csproj]";

    [Fact]
    public void A_fresh_watcher_believes_the_app_is_up()
    {
        // Ok is the only safe default: the client only shows the build panel on "failed", so a wrong
        // guess here would paint a compiler error over an app that builds fine.
        Assert.Equal(DevBuildState.Ok, new DevBuildWatcher().State);
    }

    [Fact]
    public void A_compiler_error_fails_the_build()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe(Error);

        Assert.Equal(DevBuildState.Failed, watcher.State);
        Assert.Equal([Error], watcher.Errors);
    }

    [Fact]
    public void The_same_error_reported_once_per_referencing_project_is_counted_once()
    {
        // A typo in a shared library arrives once per project that references it — three times in a
        // wasm-hosted solution. Counting them would say "3 build errors" for one typo.
        var watcher = new DevBuildWatcher();

        watcher.Observe(Error);
        watcher.Observe(Error);
        watcher.Observe(Error);

        Assert.Single(watcher.Errors);
    }

    [Fact]
    public void Distinct_errors_are_all_kept_in_report_order()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe(Error);
        watcher.Observe(OtherError);

        Assert.Equal([Error, OtherError], watcher.Errors);
    }

    [Fact]
    public void A_warning_is_not_a_failure()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe("/app/Pages/Home.cs(3,9): warning CS0168: The variable 'e' is declared but never used");

        Assert.Equal(DevBuildState.Ok, watcher.State);
        Assert.Empty(watcher.Errors);
    }

    [Fact]
    public void A_warning_whose_path_reads_like_a_rebuild_marker_does_not_clear_a_real_failure()
    {
        // The marker scan is a loose `Contains`, so a project living under a directory called "Building"
        // would otherwise erase the error the compiler had just reported one line earlier.
        var watcher = new DevBuildWatcher();

        watcher.Observe(Error);
        watcher.Observe("/src/Building/Foo.cs(1,1): warning CS0168: unused");

        Assert.Equal(DevBuildState.Failed, watcher.State);
        Assert.Single(watcher.Errors);
    }

    [Fact]
    public void Prose_that_is_not_a_diagnostic_is_ignored()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe("  Determining projects to restore...");
        watcher.Observe("info: Microsoft.Hosting.Lifetime[14]");
        watcher.Observe(null);
        watcher.Observe("   ");

        Assert.Equal(DevBuildState.Ok, watcher.State);
    }

    [Fact]
    public void A_rebuild_supersedes_the_previous_verdict()
    {
        var watcher = new DevBuildWatcher();
        watcher.Observe(Error);

        watcher.Observe("dotnet watch ⌚ File updated: /app/Pages/Home.cs");

        // Not "still broken until proven otherwise": the errors on screen belong to a build that no
        // longer describes the code on disk.
        Assert.Equal(DevBuildState.Building, watcher.State);
        Assert.Empty(watcher.Errors);
    }

    [Theory]
    [InlineData("dotnet watch ⌚ Started")]
    [InlineData("Build succeeded.")]
    [InlineData("dotnet watch 🔥 Hot reload of changes succeeded.")]
    [InlineData("dotnet watch ⌚ No managed code changes to apply.")]
    [InlineData("dotnet watch 🔥 C# and Razor changes applied in 456ms.")]
    public void A_build_that_finished_cleanly_settles_to_ok(string line)
    {
        // Every one of these is something .NET 10's watch (or MSBuild under it) actually prints on the
        // way back from a failure. Missing them all left the state on Building forever after a recovery.
        var watcher = new DevBuildWatcher();
        watcher.Observe(Error);

        watcher.Observe(line);

        Assert.Equal(DevBuildState.Ok, watcher.State);
        Assert.Empty(watcher.Errors);
    }

    [Fact]
    public void A_rebuild_that_fixes_one_of_two_errors_drops_the_one_it_fixed()
    {
        var watcher = new DevBuildWatcher();
        watcher.Observe(Error);
        watcher.Observe(OtherError);

        watcher.Observe("dotnet watch ⌚ File updated: /app/Pages/Home.cs");
        watcher.Observe(OtherError);

        Assert.Equal([OtherError], watcher.Errors);
    }

    [Fact]
    public void Watchs_own_error_form_is_recognised_even_though_it_carries_no_file_location()
    {
        // The form a failed hot-reload emit is reported in, and the one an anchored `origin :` pattern
        // silently misses — found by running `rask dev` and breaking a file, not by reading the docs.
        var watcher = new DevBuildWatcher();

        watcher.Observe("dotnet watch ❌ error CS7038: Failed to emit module 'App'.");

        Assert.Equal(DevBuildState.Failed, watcher.State);
        // Watch's decoration is dropped; a real file location is not.
        Assert.Equal("error CS7038: Failed to emit module 'App'.", Assert.Single(watcher.Errors));
    }

    [Fact]
    public void Watchs_byline_is_dropped_even_when_it_precedes_a_real_file_location()
    {
        // The shape a genuine compile error actually arrives in under watch: its byline sits in FRONT of
        // the path, so the "keep the prefix when it ends in a colon" rule would otherwise keep the emoji
        // too and put `dotnet watch ❌ /Users/…` in the panel.
        var watcher = new DevBuildWatcher();

        watcher.Observe("dotnet watch ❌ /app/A.cs(12,26): error CS0103: The name 'x' does not exist");

        Assert.Equal(
            "/app/A.cs(12,26): error CS0103: The name 'x' does not exist",
            Assert.Single(watcher.Errors));
    }

    [Fact]
    public void A_real_file_location_is_kept()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe(Error);

        Assert.Equal(Error, Assert.Single(watcher.Errors));
    }

    [Fact]
    public void Colour_codes_never_reach_the_panel()
    {
        // `rask dev` asks the child to keep emitting ANSI even though its output is redirected, so the
        // developer's terminal stays coloured. The panel renders text, and would show the escapes raw.
        var watcher = new DevBuildWatcher();

        watcher.Observe("\u001b[31m/app/A.cs(1,1): error CS0103: nope\u001b[0m");

        Assert.Equal("/app/A.cs(1,1): error CS0103: nope", Assert.Single(watcher.Errors));
    }

    [Fact]
    public void The_same_error_through_two_decorations_counts_once()
    {
        var watcher = new DevBuildWatcher();

        watcher.Observe("/app/A.cs(1,1): error CS0103: nope");
        watcher.Observe("\u001b[31m  /app/A.cs(1,1): error CS0103: nope  \u001b[0m");

        Assert.Single(watcher.Errors);
    }

    [Fact]
    public void A_failure_survives_watch_giving_up_on_it()
    {
        // What watch prints after a failed build — an exit code and a wait — must not read as recovery.
        var watcher = new DevBuildWatcher();
        watcher.Observe(Error);

        watcher.Observe("dotnet watch ❌ Exited with error code 1");
        watcher.Observe("dotnet watch ⏳ Waiting for a file to change before restarting dotnet...");

        Assert.Equal(DevBuildState.Failed, watcher.State);
    }

    // ---- the wire document ----

    [Fact]
    public void The_json_reports_ok_with_nothing_to_show()
    {
        using var doc = JsonDocument.Parse(new DevBuildWatcher().ToJson());

        Assert.Equal("ok", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void The_json_carries_the_count_the_first_error_and_all_of_them()
    {
        var watcher = new DevBuildWatcher();
        watcher.Observe(Error);
        watcher.Observe(OtherError);

        using var doc = JsonDocument.Parse(watcher.ToJson());

        Assert.Equal("failed", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(Error, doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(Error + "\n" + OtherError, doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void The_json_survives_an_error_message_containing_quotes_and_backslashes()
    {
        // Compiler messages quote identifiers, and Windows paths are full of backslashes. Hand-written
        // JSON that got this wrong would break the client's JSON.parse and show nothing at all.
        var watcher = new DevBuildWatcher();
        var nasty = """C:\src\App\A.cs(1,1): error CS0103: The name "x\y" does not exist""";
        watcher.Observe(nasty);

        using var doc = JsonDocument.Parse(watcher.ToJson());

        Assert.Equal(nasty, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Control_characters_are_escaped()
    {
        using var doc = JsonDocument.Parse("{\"v\":" + DevBuildWatcher.JsonString("a\u0001b\tc") + "}");

        Assert.Equal("a\u0001b\tc", doc.RootElement.GetProperty("v").GetString());
    }
}
