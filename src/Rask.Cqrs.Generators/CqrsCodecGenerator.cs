using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Cqrs.Generators;

/// <summary>
///     Emits the reflection-free wire codecs that let a message cross a process boundary, and registers
///     each one as a <c>RemoteContract</c>.
/// </summary>
/// <remarks>
///     <para>
///         It runs only for a compilation that references a <b>transport</b> — an assembly carrying
///         <c>[RaskCqrsTransport]</c>, which today means <c>Rask.Cqrs.Client</c> or
///         <c>Rask.Cqrs.Server</c>. That gate is the whole reason existing code is unaffected: an app
///         using Rask.Cqrs purely in-process generates nothing, so the shape rules this generator
///         enforces (RASK053) never apply to its messages.
///     </para>
///     <para>
///         Contracts are collected from the compilation itself and from any referenced assembly that
///         references Rask.Cqrs — in a hosted app, that is the shared contracts library both halves
///         compile against. Messages marked <c>[LocalOnly]</c>, directly or through an interface they
///         implement, are excluded: that is how <c>IJob</c> and <c>IOutboxEvent</c> keep whole families
///         of always-in-process messages out of the wire vocabulary.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CqrsCodecGenerator : IIncrementalGenerator
{
    private const string CqrsNamespace = "Rask.Cqrs";
    private const string CqrsAssembly = "Rask.Cqrs";

    private static readonly DiagnosticDescriptor Rask053 = new(
        "RASK053",
        "Remote message has no wire encoding",
        "Message '{0}' cannot be sent to a remote handler: {1}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A message reaches a handler in another process by being encoded, and the encoder is generated "
                     + "at compile time rather than discovered by reflection — so a shape it cannot express has to be "
                     + "reported now rather than failing on the wire. Supported: the primitive types, string, Guid, "
                     + "the date/time types, Uri, enums, byte[], nullable versions of those, arrays and lists of them, "
                     + "string-keyed dictionaries, and records or classes composed of the same. A message that is never "
                     + "sent anywhere — a job payload, an outbox event, a command only another handler publishes — "
                     + "should say so with [LocalOnly], which exempts it entirely.",
        helpLinkUri: DiagnosticHelp.Link("RASK053"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) =>

        // Deliberately driven straight off the compilation rather than through a cached model: the
        // discovery walk reaches into referenced assemblies, and the symbols it produces must not be
        // held across an incremental-pipeline boundary. Everything is done inside the output callback,
        // so nothing outlives the compilation it came from.
        context.RegisterSourceOutput(context.CompilationProvider, Execute);

    private static void Execute(SourceProductionContext spc, Compilation compilation)
    {
        if (!ReferencesTransport(compilation))
        {
            return;
        }

        var contracts = new List<ContractModel>();
        foreach (var message in DiscoverMessages(compilation))
        {
            if (spc.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            var model = Describe(message.Type, message.Kind, message.ResultType, compilation);
            if (model.Problem is { } problem)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask053,
                    message.Type.Locations.FirstOrDefault(l => l.IsInSource),
                    message.Type.ToDisplayString(),
                    problem));
                continue;
            }

            contracts.Add(model);
        }

        if (contracts.Count == 0)
        {
            return;
        }

        spc.AddSource("__RaskCqrsCodecs.g.cs", SourceText.From(Build(contracts), Encoding.UTF8));
    }

    // A transport is what makes wire encoding meaningful. Without one, generating codecs would impose
    // the contract shape rules on an app that never sends anything anywhere.
    private static bool ReferencesTransport(Compilation compilation)
    {
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attribute in reference.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass?.Name == "RaskCqrsTransportAttribute" &&
                    attributeClass.ContainingNamespace?.ToDisplayString() == CqrsNamespace)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<(INamedTypeSymbol Type, RemoteKind Kind, ITypeSymbol? ResultType)> DiscoverMessages(
        Compilation compilation)
    {
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            // Only assemblies that reference Rask.Cqrs can declare a message, so this skips the BCL and
            // every unrelated package without walking a single namespace of them.
            if (reference.Modules.Any(m => m.ReferencedAssemblies.Any(a => a.Name == CqrsAssembly)))
            {
                assemblies.Add(reference);
            }
        }

        var seen = new HashSet<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in Types(assembly.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                {
                    continue;
                }

                if (type.IsAbstract || type.IsGenericType || type.IsRecord && type.TypeKind == TypeKind.Struct)
                {
                    continue;
                }

                if (IsLocalOnly(type))
                {
                    continue;
                }

                var kind = Kind(type, out var resultType);
                if (kind is null)
                {
                    continue;
                }

                // A type visible through two references — a project reference and its own assembly, say —
                // must contribute one contract, not two.
                if (!seen.Add(type.ToDisplayString()))
                {
                    continue;
                }

                yield return (type, kind.Value, resultType);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in Types(nested))
                    {
                        yield return type;
                    }

                    break;

                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nestedType in type.GetTypeMembers())
                    {
                        yield return nestedType;
                    }

                    break;
            }
        }
    }

    // [LocalOnly] on the message, or on any interface it implements. The interface form is what lets one
    // line in Rask.Jobs keep every job payload in-process.
    private static bool IsLocalOnly(INamedTypeSymbol type)
    {
        if (HasLocalOnly(type))
        {
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (HasLocalOnly(@interface))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLocalOnly(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "LocalOnlyAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == CqrsNamespace);

    private static RemoteKind? Kind(INamedTypeSymbol type, out ITypeSymbol? resultType)
    {
        resultType = null;
        RemoteKind? kind = null;

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.ContainingNamespace?.ToDisplayString() != CqrsNamespace)
            {
                continue;
            }

            switch (@interface.MetadataName)
            {
                case "IQuery`1":
                    resultType = @interface.TypeArguments[0];
                    return RemoteKind.Query;

                case "ICommand`1":
                    resultType = @interface.TypeArguments[0];
                    return RemoteKind.ResultCommand;

                // Keep looking: ICommand<T> also implies nothing about ICommand, but a type may implement
                // both INotification and ICommand, and the more specific shape should win.
                case "ICommand":
                    kind ??= RemoteKind.VoidCommand;
                    break;

                case "INotification":
                    kind ??= RemoteKind.Notification;
                    break;
            }
        }

        return kind;
    }

    private static ContractModel Describe(
        INamedTypeSymbol type,
        RemoteKind kind,
        ITypeSymbol? resultType,
        Compilation compilation)
    {
        var message = WireShape.Classify(type, allowFile: true);
        if (message.Kind == WireKind.Unsupported)
        {
            return ContractModel.Failed(message.Reason!);
        }

        var returnsFile = resultType is not null && IsFileDownload(resultType);
        WireType? result = null;
        if (resultType is not null && !returnsFile)
        {
            result = WireShape.Classify(resultType, allowFile: false);
            if (result.Kind == WireKind.Unsupported)
            {
                return ContractModel.Failed(
                    $"its result type '{resultType.ToDisplayString()}' {result.Reason}");
            }
        }

        return new ContractModel
        {
            Type = type,
            Kind = kind,
            Message = message,
            Result = result,
            ResultFqn = returnsFile
                ? "global::Rask.Cqrs.FileDownload"
                : resultType is null
                    ? "global::Rask.Cqrs.Unit"
                    : resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ReturnsFile = returnsFile,
            CarriesFiles = message.ContainsFile,
            WireName = type.ToDisplayString(),
        };
    }

    private static bool IsFileDownload(ITypeSymbol type) =>
        type.Name == "FileDownload" && type.ContainingNamespace?.ToDisplayString() == CqrsNamespace;

    private static string Build(List<ContractModel> contracts)
    {
        var emitter = new WireCodecEmitter();
        var registrations = new List<(string Field, string Declaration)>();

        foreach (var contract in contracts.OrderBy(c => c.WireName, System.StringComparer.Ordinal))
        {
            var messageId = emitter.Ensure(contract.Message);
            var resultId = contract.Result is null ? null : emitter.Ensure(contract.Result);

            var field = "C" + registrations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var entry = new StringBuilder();
            entry.AppendLine($"    private static readonly global::Rask.Cqrs.RemoteContract {field} =");
            entry.AppendLine("        new global::Rask.Cqrs.RemoteContract");
            entry.AppendLine("        {");
            entry.AppendLine($"            MessageType = typeof({contract.Message.Fqn}),");
            entry.AppendLine($"            Name = \"{contract.WireName}\",");
            entry.AppendLine($"            Kind = global::Rask.Cqrs.RemoteMessageKind.{contract.Kind},");
            entry.AppendLine($"            ResultType = typeof({contract.ResultFqn}),");
            entry.AppendLine(
                $"            WriteMessage = static (writer, message, files) => W{messageId}(writer, "
                + $"({contract.Message.Fqn})message, files),");
            entry.AppendLine(
                $"            ReadMessage = static (ref {Reader} reader, {FileListRead} files) => "
                + $"R{messageId}(ref reader, files, \"{contract.WireName}\"),");

            if (resultId is not null)
            {
                entry.AppendLine(
                    $"            WriteResult = static (writer, result) => W{resultId}(writer, "
                    + $"({contract.Result!.Fqn})result, NoFiles),");
                entry.AppendLine(
                    $"            ReadResult = static (ref {Reader} reader) => "
                    + $"R{resultId}(ref reader, NoFiles, \"result\"),");
            }

            entry.AppendLine($"            CarriesFiles = {(contract.CarriesFiles ? "true" : "false")},");
            entry.AppendLine($"            ReturnsFile = {(contract.ReturnsFile ? "true" : "false")},");
            // A request's invoker is emitted closed over the concrete result type, which is what lets a
            // client hand back a real Task<TResult> without MakeGenericType. Notifications need none:
            // IRemoteDispatch.PublishAsync is not generic, so a transport calls it directly.
            if (contract.Kind != RemoteKind.Notification)
            {
                var send = contract.Kind == RemoteKind.VoidCommand
                    ? $"Remote(provider).SendAsync({field}, message, cancellationToken)"
                    : $"Remote(provider).SendAsync<{contract.ResultFqn}>({field}, message, cancellationToken)";
                entry.AppendLine(
                    $"            Invoker = static (provider, message, cancellationToken) => {send},");
            }

            entry.AppendLine("        };");
            entry.AppendLine();
            registrations.Add((field, entry.ToString()));
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable disable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskCqrsCodecs");
        sb.AppendLine("{");
        sb.AppendLine("    // Results never carry files, so their codecs are handed a list that can be shared and");
        sb.AppendLine("    // never written to.");
        sb.AppendLine("    private static readonly global::Rask.Cqrs.RemoteFile[] NoFiles = new global::Rask.Cqrs.RemoteFile[0];");
        sb.AppendLine();
        sb.Append(emitter.Methods);

        sb.AppendLine("    // Resolved per dispatch rather than captured: a transport can be a scoped service,");
        sb.AppendLine("    // and a contract is a static that outlives every scope.");
        sb.AppendLine("    private static global::Rask.Cqrs.IRemoteDispatch Remote(global::System.IServiceProvider provider) =>");
        sb.AppendLine("        provider.GetService(typeof(global::Rask.Cqrs.IRemoteDispatch)) as global::Rask.Cqrs.IRemoteDispatch");
        sb.AppendLine("        ?? throw new global::System.InvalidOperationException(");
        sb.AppendLine("            \"This message has no handler in this process and no transport to send it through. \"");
        sb.AppendLine("            + \"Call AddRaskCqrsClient() during startup, or give the message a handler here.\");");
        sb.AppendLine();

        foreach (var (_, declaration) in registrations)
        {
            sb.Append(declaration);
        }

        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Initialize()");
        sb.AppendLine("    {");
        sb.AppendLine("        global::Rask.Cqrs.RemoteContractRegistry.Replace(");
        sb.AppendLine("            typeof(__RaskCqrsCodecs),");
        sb.AppendLine("            new global::Rask.Cqrs.RemoteContract[]");
        sb.AppendLine("            {");
        foreach (var (field, _) in registrations)
        {
            sb.AppendLine($"                {field},");
        }

        sb.AppendLine("            });");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private const string Reader = "global::System.Text.Json.Utf8JsonReader";
    private const string FileListRead = "global::System.Collections.Generic.IReadOnlyList<global::Rask.Cqrs.RemoteFile>";

    private enum RemoteKind
    {
        Query,
        VoidCommand,
        ResultCommand,
        Notification,
    }

    private sealed class ContractModel
    {
        public INamedTypeSymbol Type { get; set; } = null!;

        public RemoteKind Kind { get; set; }

        public WireType Message { get; set; } = null!;

        public WireType? Result { get; set; }

        public string ResultFqn { get; set; } = string.Empty;

        public string WireName { get; set; } = string.Empty;

        public bool CarriesFiles { get; set; }

        public bool ReturnsFile { get; set; }

        public string? Problem { get; set; }

        public static ContractModel Failed(string problem) => new() { Problem = problem };
    }
}
