using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// How <c>rask generate feature</c> splices its DI into a <c>Program.cs</c> that already has some.
/// </summary>
/// <remarks>
/// The interesting cases all come from combining <c>rask new</c>'s battery flags with a later
/// <c>rask generate</c>: the file the splice edits is no longer the minimal one it was written against.
/// </remarks>
public sealed class ProgramSpliceOutboxTests
{
    private const string Header = """
        using Rask.Server;

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRask();

        """;

    private const string OutboxRegistration =
        "builder.Services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);";

    [Fact]
    public void A_multi_line_registration_is_recognised_and_not_duplicated()
    {
        // `rask new --outbox` emits AddRaskData over several lines. Comparing whole first lines missed it,
        // so a later `rask generate feature` appended a second AddRaskData — and since AddRaskData is
        // guarded so the FIRST call wins, the duplicate's options would have been silently dropped.
        var program = Header + """
            builder.Services.AddRaskData(o =>
            {
                o.DispatchDomainEventsInProcess = false;
            });
            """;

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], ["builder.Services.AddRaskData();"]);

        Assert.Empty(added);
        Assert.Same(program, text);
        Assert.Equal(1, CountOccurrences(text, "builder.Services.AddRaskData"));
    }

    [Fact]
    public void Adding_an_outbox_feature_upgrades_a_bare_AddRaskData()
    {
        // The reachable trap: `rask new App --data` writes the bare call, then `rask g f Order --outbox`
        // needs the in-process publisher off. Appending the outbox-safe call second would do nothing —
        // the guard keeps the first registration — leaving DomainEventInterceptor registered, so it drains
        // and clears every entity's events before OutboxInterceptor can copy them. The outbox table stays
        // empty, delivery quietly stops being durable, and every handler still runs so nothing looks wrong.
        var program = Header + "builder.Services.AddRaskData();";

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], [OutboxRegistration]);

        Assert.Contains(OutboxRegistration, text, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Services.AddRaskData();", text, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(text, "builder.Services.AddRaskData"));
        Assert.Single(added);
    }

    [Fact]
    public void A_customised_AddRaskData_is_left_alone()
    {
        // Someone who has already configured AddRaskData keeps their version — the CLI upgrades the bare
        // scaffolded call, it doesn't rewrite hand-written configuration.
        var program = Header + """
            builder.Services.AddRaskData(o =>
            {
                o.DispatchDomainEventsInProcess = true;
            });
            """;

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], [OutboxRegistration]);

        Assert.Same(program, text);
        Assert.Empty(added);
    }

    [Fact]
    public void An_already_outbox_safe_registration_is_not_touched_twice()
    {
        var program = Header + OutboxRegistration;

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], [OutboxRegistration]);

        Assert.Same(program, text);
        Assert.Empty(added);
        Assert.Equal(1, CountOccurrences(text, "builder.Services.AddRaskData"));
    }

    [Fact]
    public void A_second_feature_does_not_re_register_the_mediator()
    {
        var program = Header + "builder.Services.AddRaskCqrs();";

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], ["builder.Services.AddRaskCqrs();"]);

        Assert.Same(program, text);
        Assert.Empty(added);
    }

    [Fact]
    public void A_genuinely_new_registration_is_still_appended()
    {
        var program = Header + "builder.Services.AddRaskCqrs();";

        var (text, added) = GenerateCommand.SpliceProgramCs(program, [], ["builder.Services.AddRaskData();"]);

        Assert.Contains("builder.Services.AddRaskData();", text, StringComparison.Ordinal);
        Assert.Single(added);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
