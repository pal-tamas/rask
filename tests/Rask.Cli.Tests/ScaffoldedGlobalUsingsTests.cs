using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Every battery is reached through a static facade — <c>Cache.Remember(key, load)</c>,
///     <c>Mail.Send(email)</c>, <c>Jobs.Enqueue(job)</c> — and each one lives in its own namespace, because
///     a type named <c>Cache</c> cannot share a name with the namespace <c>Rask.Cache</c> it would then sit
///     in. So the facades cannot ride on the <c>using Rask;</c> that carries <c>Ui</c>, and without a global
///     using of their own the line every guide shows would not compile in a scaffolded app.
/// </summary>
/// <remarks>
///     Asserted here rather than left to the build gates because NOTHING scaffolded calls a facade: the
///     templates only mention them in comments, and the tutorial chapters that do call them are the elided
///     snippets the tutorial walk cannot write as files. The gap was invisible to every gate in the repo.
/// </remarks>
public class ScaffoldedGlobalUsingsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rask-globalusings-tests");

    [Fact]
    public void A_battery_that_is_on_is_global_used_so_its_facade_needs_no_import()
    {
        var usings = GlobalUsings(new ServerBatteries
        {
            Data = true,
            Cqrs = true,
            Jobs = true,
            Mail = true,
            Cache = true,
            Storage = true,
            Logs = true,
        });

        Assert.Contains("global using Rask.Jobs;", usings, StringComparison.Ordinal);
        Assert.Contains("global using Rask.Mail;", usings, StringComparison.Ordinal);
        Assert.Contains("global using Rask.Cache;", usings, StringComparison.Ordinal);
        Assert.Contains("global using Rask.Storage;", usings, StringComparison.Ordinal);
        Assert.Contains("global using Rask.Logging;", usings, StringComparison.Ordinal);
        Assert.Contains("global using Rask.Cqrs;", usings, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battery_that_is_off_brings_no_namespace_the_app_cannot_use()
    {
        var usings = GlobalUsings(new ServerBatteries { Data = true, Cqrs = true });

        Assert.DoesNotContain("global using Rask.Jobs;", usings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Rask.Mail;", usings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Rask.Cache;", usings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Rask.Storage;", usings, StringComparison.Ordinal);
    }

    [Fact]
    public void The_framework_front_door_is_there_whatever_is_switched_off()
    {
        var usings = GlobalUsings(new ServerBatteries());

        Assert.Contains("global using Rask;", usings, StringComparison.Ordinal);
        Assert.Contains("global using static Rask.Markup;", usings, StringComparison.Ordinal);
        Assert.DoesNotContain("rask:if", usings, StringComparison.Ordinal);
    }

    private static string GlobalUsings(ServerBatteries batteries)
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", batteries, "1.0.0");
        var file = result.Files.Single(f => Path.GetFileName(f.Path) == "GlobalUsings.cs");
        return file.Content;
    }
}
