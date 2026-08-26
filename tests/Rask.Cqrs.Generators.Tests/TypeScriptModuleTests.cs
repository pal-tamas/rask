using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Cqrs.Generators.Tests;

/// <summary>
///     The two files a front end actually imports: the types, and the message factories.
/// </summary>
/// <remarks>
///     <see cref="TypeScriptEmitterTests" /> covers the per-type mapping. These cover what the module
///     puts around it — the imports, the factory shape, and the descriptor the client revives dates
///     against.
/// </remarks>
public class TypeScriptModuleTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        """;

    /// <summary>Builds the module for one message type, optionally with a result type.</summary>
    private static (string Contracts, string Messages) Build(
        string source,
        string messageType = "GetOrder",
        string? resultType = null,
        string kind = "query",
        bool returnsFile = false)
    {
        var tree = CSharpSyntaxTree.ParseText(Preamble + source);
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            "Probe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        INamedTypeSymbol Symbol(string name) =>
            compilation.GetTypeByMetadataName(name)
            ?? throw new InvalidOperationException($"'{name}' did not compile.");

        var message = Symbol(messageType);
        var contract = new TypeScriptContract
        {
            WireName = message.ToDisplayString(),
            Kind = kind,
            Message = WireShape.Classify(message, allowFile: true),
            Result = resultType is null ? null : WireShape.Classify(Symbol(resultType), allowFile: false),
            ReturnsFile = returnsFile,
            FileProperties = [],
        };

        var module = TypeScriptModule.Build([contract]);
        return (module.Contracts, module.Messages);
    }

    [Fact]
    public void A_query_becomes_a_camel_cased_factory_bound_to_its_wire_name()
    {
        var (_, messages) = Build(
            """
            public sealed record Order(Guid Id);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        // The wire name is the full CLR name, because that is what the server routes on. The factory
        // is named after the message's leaf, lower-cased so it does not collide with the interface of
        // the same name that contracts.ts exports.
        Assert.Contains("export const getOrder = message<GetOrder, Order, 'query'>({", messages, StringComparison.Ordinal);
        Assert.Contains("name: 'GetOrder',", messages, StringComparison.Ordinal);
        Assert.Contains("kind: 'query',", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_with_no_result_answers_void()
    {
        var (_, messages) = Build("public sealed record GetOrder(Guid Id);", kind: "command");

        Assert.Contains("message<GetOrder, void, 'command'>", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_answer_becomes_a_download_rather_than_a_parsed_body()
    {
        var (_, messages) = Build("public sealed record GetOrder(Guid Id);", returnsFile: true);

        Assert.Contains("message<GetOrder, RaskDownload, 'query'>", messages, StringComparison.Ordinal);
        Assert.Contains("returnsFile: true,", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_types_a_factory_names_are_imported()
    {
        var (_, messages) = Build(
            """
            public sealed record Order(Guid Id);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        Assert.Contains("import { message, registerShapes } from './client';", messages, StringComparison.Ordinal);
        Assert.Contains("import type { GetOrder, Order } from './contracts';", messages, StringComparison.Ordinal);

        // Importing this module is what arms date revival — the client never imports the generated
        // table itself, so that it keeps type-checking in an app that has not built yet.
        Assert.Contains("registerShapes(shapes);", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void A_built_in_answer_is_not_imported_as_a_type()
    {
        // The failure this prevents is a compile error in the client, not a runtime one: `import type
        // { number }` does not resolve, and the whole generated module stops type-checking.
        var (_, messages) = Build("public sealed record GetOrder(Guid Id);", returnsFile: true);

        Assert.DoesNotContain("RaskDownload }", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskDownload,", messages.Split('\n')[2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_descriptor_names_the_instants_the_client_revives()
    {
        var (contracts, _) = Build(
            """
            public sealed record Line(DateTimeOffset ShippedAt);
            public sealed record Order(DateTimeOffset PlacedAt, DateOnly DeliverBy, List<Line> Lines);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        // placedAt is an instant, so it is revived. deliverBy is a calendar date and must NOT be — a
        // Date would put anyone west of UTC on the previous day. lines leads to another shape, so the
        // client recurses rather than guessing.
        Assert.Contains("Order: { instants: ['placedAt'], nested: { lines: ['Line', 1] } },", contracts, StringComparison.Ordinal);
        Assert.Contains("Line: { instants: ['shippedAt'], nested: {} },", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_factory_names_the_shape_its_answer_revives_against()
    {
        var (_, messages) = Build(
            """
            public sealed record Order(DateTimeOffset PlacedAt);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        Assert.Contains("result: ['Order', 0],", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_with_nothing_to_revive_names_no_shape()
    {
        var (_, messages) = Build(
            """
            public sealed record Order(string Name);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        // Carrying it would make the client walk every property of every response of that type to
        // discover there is nothing to do.
        Assert.DoesNotContain("result:", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shape_with_nothing_to_revive_is_left_out_of_the_descriptor()
    {
        var (contracts, _) = Build(
            """
            public sealed record Order(string Name, int Quantity);
            public sealed record GetOrder(Guid Id);
            """,
            resultType: "Order");

        // Carrying an empty entry costs the client a walk over every property of every response for
        // no possible effect.
        Assert.DoesNotContain("Order: {", contracts, StringComparison.Ordinal);
        Assert.Contains("export const shapes = {", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_string_aliases_say_what_they_are()
    {
        var (contracts, _) = Build("public sealed record GetOrder(Guid Id, DateOnly On);");

        Assert.Contains("export type Guid = string;", contracts, StringComparison.Ordinal);
        Assert.Contains("export type DateOnly = string;", contracts, StringComparison.Ordinal);
        Assert.Contains("export type Duration = string;", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_files_are_marked_generated()
    {
        var (contracts, messages) = Build("public sealed record GetOrder(Guid Id);");

        // These land inside the front end's own source tree, where the only thing standing between a
        // developer and a lost edit is the first line of the file.
        Assert.StartsWith("// <auto-generated>", contracts, StringComparison.Ordinal);
        Assert.StartsWith("// <auto-generated>", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void Factories_are_ordered_by_wire_name()
    {
        var tree = CSharpSyntaxTree.ParseText(Preamble + """
            public sealed record Zebra(Guid Id);
            public sealed record Alpha(Guid Id);
            """);
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();
        var compilation = CSharpCompilation.Create("Probe", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        List<TypeScriptContract> contracts =
        [
            Contract(compilation, "Zebra"),
            Contract(compilation, "Alpha"),
        ];

        var messages = TypeScriptModule.Build(contracts).Messages;

        // A generated file that reorders itself between builds produces a diff on every build and
        // makes any review of it worthless.
        Assert.True(
            messages.IndexOf("export const alpha", StringComparison.Ordinal)
            < messages.IndexOf("export const zebra", StringComparison.Ordinal));
    }

    private static TypeScriptContract Contract(CSharpCompilation compilation, string name)
    {
        var symbol = compilation.GetTypeByMetadataName(name)!;
        return new TypeScriptContract
        {
            WireName = symbol.ToDisplayString(),
            Kind = "query",
            Message = WireShape.Classify(symbol, allowFile: true),
            FileProperties = [],
        };
    }
}
