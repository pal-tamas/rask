using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

// An [OffThread] message crosses to the app's own Web Worker in its generated encoding, so it gets a codec even in an
// app that talks to no server — and what cannot cross is a build error rather than a worker that fails at runtime.
public sealed class OffThreadCodecTests
{
    private static readonly string[] Transports = ["Rask.Cqrs.Client", "Rask.Cqrs.Server"];

    [Fact]
    public void An_off_thread_query_gets_a_codec_without_a_transport_and_an_ordinary_one_does_not()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Cqrs;
            namespace Demo;

            [OffThread]
            public sealed record ReadSheet(byte[] File) : IQuery<int>;
            public sealed class ReadSheetHandler : IQueryHandler<ReadSheet, int>
            {
                public Task<int> Handle(ReadSheet q) => Task.FromResult(q.File.Length);
            }

            public sealed record CountRows(int Sheet) : IQuery<int>;
            public sealed class CountRowsHandler : IQueryHandler<CountRows, int>
            {
                public Task<int> Handle(CountRows q) => Task.FromResult(q.Sheet);
            }
            """;

        var run = GeneratorHarness.RunWithout(source, new CqrsCodecGenerator(), Transports, "Rask.Cqrs", "Rask.Wire");

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("typeof(global::Demo.ReadSheet)", codecs, StringComparison.Ordinal);
        Assert.Contains("LocalInvoker = ", codecs, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Demo.CountRows", codecs, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_array_is_written_beside_the_json_when_the_sender_asks_and_read_in_either_form()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Cqrs;
            namespace Demo;

            [OffThread]
            public sealed record ReadSheet(byte[] File) : IQuery<int>;
            public sealed class ReadSheetHandler : IQueryHandler<ReadSheet, int>
            {
                public Task<int> Handle(ReadSheet q) => Task.FromResult(q.File.Length);
            }
            """;

        var run = GeneratorHarness.RunWithout(source, new CqrsCodecGenerator(), Transports, "Rask.Cqrs", "Rask.Wire");

        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("files is global::System.Collections.ObjectModel.Collection<global::Rask.Wire.RemoteFile>", codecs, StringComparison.Ordinal);
        Assert.Contains("writer.WriteBase64StringValue(value);", codecs, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Wire.WireJson.ResolveFile(", codecs, StringComparison.Ordinal);
    }

    [Fact]
    public void An_off_thread_result_with_no_wire_encoding_is_RASK123()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Cqrs;
            namespace Demo;

            public interface ISheet { int Rows { get; } }

            [OffThread]
            public sealed record ReadSheet(byte[] File) : IQuery<ISheet>;
            public sealed class ReadSheetHandler : IQueryHandler<ReadSheet, ISheet>
            {
                public Task<ISheet> Handle(ReadSheet q) => Task.FromResult<ISheet>(null!);
            }
            """;

        var run = GeneratorHarness.RunWithout(source, new CqrsCodecGenerator(), Transports, "Rask.Cqrs", "Rask.Wire");

        var reported = Assert.Single(run.Diagnostics, static d => d.Id == "RASK123");
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("has no wire encoding", reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK053");
    }

    [Fact]
    public void An_off_thread_handler_that_asks_for_a_page_service_is_RASK123()
    {
        var source = """
            using System.Threading.Tasks;
            using Microsoft.JSInterop;
            using Rask.Cqrs;
            namespace Demo;

            [OffThread]
            public sealed record ReadSheet(byte[] File) : IQuery<int>;
            public sealed class ReadSheetHandler(IJSRuntime js) : IQueryHandler<ReadSheet, int>
            {
                public Task<int> Handle(ReadSheet q) => Task.FromResult(js is null ? 0 : q.File.Length);
            }
            """;

        var run = GeneratorHarness.RunWithout(
            source, new CqrsCodecGenerator(), Transports, "Rask.Cqrs", "Rask.Wire", "Microsoft.JSInterop");

        var reported = Assert.Single(run.Diagnostics, static d => d.Id == "RASK123");
        var message = reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("ReadSheetHandler", message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.JSInterop.IJSRuntime", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_off_thread_handler_that_asks_only_for_what_the_worker_has_is_quiet()
    {
        var source = """
            using System.Net.Http;
            using System.Threading.Tasks;
            using Rask.Cqrs;
            namespace Demo;

            [OffThread]
            public sealed record ReadSheet(byte[] File) : IQuery<int>;
            public sealed class ReadSheetHandler(HttpClient http) : IQueryHandler<ReadSheet, int>
            {
                public Task<int> Handle(ReadSheet q) => Task.FromResult(http is null ? 0 : q.File.Length);
            }
            """;

        var run = GeneratorHarness.RunWithout(source, new CqrsCodecGenerator(), Transports, "Rask.Cqrs", "Rask.Wire");

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK123");
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
