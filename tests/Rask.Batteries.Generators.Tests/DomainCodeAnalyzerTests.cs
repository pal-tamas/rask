using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK140, RASK141 and RASK142: the machine's clock and a looked-up service in domain code, and side-by-side work in a transaction.</summary>
public class DomainCodeAnalyzerTests
{
    // ---- RASK140 ----

    private const string Clocks = """
        using System;
        using Rask;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed class Delivery : Aggregate<Guid>
        {
            private Delivery() { }
            public DateTime Stamped { get; private set; }
            public void Stamp()
            {
                Stamped = DateTime.Now;
                var day = DateTime.Today;
                var utc = DateTime.UtcNow;
                var at = DateTimeOffset.Now;
                var atUtc = DateTimeOffset.UtcNow;
                var local = Stamped.ToLocalTime();
                var fine = Clock.Now;
            }
            public sealed class Nested
            {
                public static DateTime When() => DateTime.Now;
            }
        }
        public sealed partial record Opening(DateOnly? Date) : Value
        {
            public bool Late => Date < DateOnly.FromDateTime(DateTime.Today);
        }
        public static class Elsewhere
        {
            public static DateTime When() => DateTime.Now;
        }
        """;

    [Fact]
    public async Task Each_read_of_the_machines_clock_in_domain_code_is_a_RASK140_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new SystemClockAnalyzer(), Clocks);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(
            ["DateTime.Now", "DateTime.Today", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "Stamped.ToLocalTime()", "DateTime.Now", "DateTime.Today"],
            flagged);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        Assert.Contains("'DateTime.Now' in 'Delivery' reads the machine's clock", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("use 'Clock.Wall'", diagnostics[0].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_the_machines_clock()
    {
        var strict = StrictConfig.Severity("RASK140");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new SystemClockAnalyzer(), Clocks, options => options.WithSpecificDiagnosticOptions([new("RASK140", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(8, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public async Task The_machines_clock_outside_domain_code_is_not_RASK140()
    {
        var source = "using System; namespace Shop; public static class Report { public static DateTime When() => DateTime.Now.ToLocalTime(); }";

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new SystemClockAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    // ---- RASK141 ----

    private const string Services = """
        using System;
        using Rask;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public interface IPrices { decimal Of(string sku); }
        public sealed class Order : Aggregate<Guid>
        {
            private Order() { }
            public decimal Total { get; private set; }
            public void Price(string sku) => Total = Current.Service<IPrices>().Of(sku);
            public void Price(string sku, IPrices prices) => Total = prices.Of(sku);
        }
        public sealed partial record Sku(string Value) : Text(Value, MaxLength: 20)
        {
            public decimal Price => Current.Service<IPrices>().Of(Value);
        }
        public static class Checkout
        {
            public static decimal Price(string sku) => Current.Service<IPrices>().Of(sku);
        }
        """;

    [Fact]
    public async Task A_service_an_aggregate_or_a_value_object_looks_up_is_a_RASK141_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new DomainServiceLocatorAnalyzer(), Services);

        var messages = diagnostics.Select(d => d.GetMessage()).ToList();

        Assert.Equal(2, messages.Count);
        Assert.StartsWith("'Order' reaches for 'IPrices' with Current.Service", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("'Sku' reaches for 'IPrices'", messages[1], StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_looked_up_service()
    {
        var strict = StrictConfig.Severity("RASK141");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new DomainServiceLocatorAnalyzer(), Services, options => options.WithSpecificDiagnosticOptions([new("RASK141", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    // ---- RASK142 ----

    private static string Transaction(string work) => $$"""
        using System;
        using System.Linq;
        using System.Net.Http;
        using System.Threading.Tasks;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed class Tank : Aggregate<Guid>
        {
            private Tank() { }
            public static Task<int> Count() => Task.FromResult(0);
            public static Task<Tank?> Load(Guid id) => Task.FromResult<Tank?>(null);
        }
        // The shape the generated reads and writes take: an extension block over the aggregate.
        public static class TankModelExtensions
        {
            extension(Tank)
            {
                public static Task<Tank?> Find(Guid id) => Task.FromResult<Tank?>(null);
            }
            extension(Tank tank)
            {
                public Task Save() => Task.CompletedTask;
            }
        }
        public static class Moves
        {
            public static async Task Run(Guid[] ids, HttpClient http)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("await Db.Transaction(async () => { await Task.WhenAll(Tank.Count(), Tank.Count()); });")]
    [InlineData("await Db.Transaction(async () => { var a = Tank.Count(); var b = Tank.Load(ids[0]); await Task.WhenAll(a, b); });")]
    [InlineData("await Db.Transaction(async () => { await Task.WhenAll(ids.Select(id => Tank.Load(id))); });")]
    [InlineData("var n = await Db.Transaction(async () => { var all = await Task.WhenAll(ids.Select(Tank.Load)); return all.Length; });")]
    [InlineData("await Db.Transaction(async () => { await Task.WhenAll(Tank.Find(ids[0]), Tank.Find(ids[1])); });")]
    [InlineData("await Db.Transaction(async () => { var t = (await Tank.Find(ids[0]))!; await Task.WhenAll(t.Save(), t.Save()); });")]
    [InlineData("await Db.Transaction(async () => { await Task.Run(async () => await Task.WhenAll(Tank.Count(), Tank.Count())); });")]
    public async Task Database_work_run_side_by_side_inside_a_transaction_is_RASK142(string work)
    {
        var source = Transaction(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ConcurrentTransactionWorkAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.StartsWith("Task.WhenAll(", flagged.Flagged(), StringComparison.Ordinal);
        Assert.Contains("starts 'Tank.", flagged.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("await Task.WhenAll(Tank.Count(), Tank.Count());")]
    [InlineData("await Db.Transaction(async () => { await Tank.Count(); await Tank.Count(); });")]
    [InlineData("await Db.Transaction(async () => { await Task.WhenAll(http.GetAsync(\"a\"), http.GetAsync(\"b\")); });")]
    public async Task Work_one_after_the_other_or_outside_a_transaction_is_not_RASK142(string work)
    {
        var source = Transaction(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ConcurrentTransactionWorkAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_side_by_side_work_in_a_transaction()
    {
        var strict = StrictConfig.Severity("RASK142");
        var source = Transaction("await Db.Transaction(async () => { await Task.WhenAll(Tank.Count(), Tank.Count()); });");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ConcurrentTransactionWorkAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK142", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
