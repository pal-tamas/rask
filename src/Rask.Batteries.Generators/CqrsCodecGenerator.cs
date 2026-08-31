using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

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
///         implement, are excluded: that is how <c>IBackgroundJob</c> and <c>IOutboxEvent</c> keep whole families
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
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // A front end only exists for a project that asked for one, and the TypeScript is carried out
        // of the compiler inside the assembly — so this stays off unless the build opts in. Reading it
        // through its own provider keeps the flag out of the compilation-shaped cache key.
        var typeScript = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.RaskEmitTypeScript", out var value)
            && value.Equals("true", System.StringComparison.OrdinalIgnoreCase));

        // Deliberately driven straight off the compilation rather than through a cached model: the
        // discovery walk reaches into referenced assemblies, and the symbols it produces must not be
        // held across an incremental-pipeline boundary. Everything is done inside the output callback,
        // so nothing outlives the compilation it came from.
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(typeScript),
            static (spc, pair) => Execute(spc, pair.Left, pair.Right));
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation, bool emitTypeScript)
    {
        if (!ReferencesTransport(compilation))
        {
            return;
        }

        var handlers = DiscoverHandlers(compilation);

        var contracts = new List<ContractModel>();
        foreach (var message in DiscoverMessages(compilation))
        {
            if (spc.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            var model = Describe(message.Type, message.Kind, message.ResultType, compilation);
            if (model.Problem is null)
            {
                handlers.TryGetValue(message.Type.ToDisplayString(), out var handler);
                model.HasLocalHandler = handler is not null;
                var authorization = Authorization(handler);
                model.Policy = authorization.Policy;
                model.Roles = authorization.Roles;
                model.AllowAnonymous = authorization.AllowAnonymous;
            }

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

        if (emitTypeScript)
        {
            spc.AddSource("__RaskExternal.g.cs", SourceText.From(TypeScript(contracts), Encoding.UTF8));
        }
    }

    /// <summary>
    ///     Carries the generated TypeScript out of the compiler as two string constants.
    /// </summary>
    /// <remarks>
    ///     A source generator cannot write to disk — it has no build directory to write into, and an
    ///     incremental run may be cancelled halfway through. So the text rides in the assembly and an
    ///     MSBuild task reads it back out of the metadata afterwards. The constants are internal:
    ///     nothing consumes them from C#, and public ones would put the whole front end into the app's
    ///     API surface.
    /// </remarks>
    private static string TypeScript(List<ContractModel> contracts)
    {
        var module = TypeScriptModule.Build(contracts.ConvertAll(Reduce));

        return "// <auto-generated/>\n"
               + "#nullable enable\n"
               + "namespace Rask.Cqrs.Generated;\n\n"
               + "internal static class RaskGeneratedTypeScript\n{\n"
               + "    public const string Contracts = " + Literal(module.Contracts) + ";\n\n"
               + "    public const string Messages = " + Literal(module.Messages) + ";\n}\n";
    }

    // Escaped rather than emitted as a raw string literal: the TypeScript carries quotes and braces of
    // its own, and an escaped literal cannot be broken by anything a contract's doc comment contains.
    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    private static TypeScriptContract Reduce(ContractModel contract) => new()
    {
        WireName = contract.WireName,
        Kind = contract.Kind switch
        {
            RemoteKind.Query => "query",
            RemoteKind.Notification => "notification",
            _ => "command",
        },
        Message = contract.Message,
        Result = contract.Result,
        ReturnsFile = contract.ReturnsFile,
        FileProperties = FileProperties(contract.Message),
    };

    /// <summary>The wire names of the message's file-carrying properties, in declaration order.</summary>
    /// <remarks>
    ///     Order is the contract: the server pairs a multipart part with a property by the index the
    ///     message reserved for it, so getting the order wrong does not fail — it hands the handler
    ///     somebody else's file.
    /// </remarks>
    private static List<string> FileProperties(WireType message)
    {
        var names = new List<string>();
        foreach (var member in message.Members)
        {
            if (member.Type.ContainsFile)
            {
                names.Add(member.WireName);
            }
        }

        return names;
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
        var seen = new HashSet<string>();
        foreach (var assembly in MessageAssemblies(compilation))
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

    // Maps a request type to the handler that handles it here, so the endpoint can read the handler's
    // [Authorize] without knowing the handler's type at runtime. Only the compilation being built is
    // scanned plus the assemblies it shares a message vocabulary with — which is where handlers live.
    private static Dictionary<string, INamedTypeSymbol> DiscoverHandlers(Compilation compilation)
    {
        var map = new Dictionary<string, INamedTypeSymbol>();

        foreach (var assembly in MessageAssemblies(compilation))
        {
            foreach (var type in Types(assembly.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                {
                    continue;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (iface.ContainingNamespace?.ToDisplayString() != CqrsNamespace || !iface.IsGenericType)
                    {
                        continue;
                    }

                    var handles = iface.MetadataName is "IQueryHandler`2" or "ICommandHandler`1"
                        or "ICommandHandler`2" or "INotificationHandler`1";

                    if (handles && iface.TypeArguments.Length > 0)
                    {
                        map[iface.TypeArguments[0].ToDisplayString()] = type;
                    }
                }
            }
        }

        return map;
    }

    // Matched by name so this generator needs no reference to ASP.NET. Roles is read as well as Policy
    // because dropping it silently would leave an author believing [Authorize(Roles = "admin")] was
    // enforced when nothing checked it.
    private static (string? Policy, string? Roles, bool AllowAnonymous) Authorization(INamedTypeSymbol? handler)
    {
        if (handler is null)
        {
            return (null, null, false);
        }

        string? policy = null;
        string? roles = null;
        var anonymous = false;

        foreach (var attribute in handler.GetAttributes())
        {
            switch (attribute.AttributeClass?.Name)
            {
                case "AllowAnonymousAttribute":
                    anonymous = true;
                    break;

                case "AuthorizeAttribute":
                    if (attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is string positional)
                    {
                        policy = positional;
                    }

                    foreach (var named in attribute.NamedArguments)
                    {
                        if (named.Key == "Policy" && named.Value.Value is string p)
                        {
                            policy = p;
                        }
                        else if (named.Key == "Roles" && named.Value.Value is string r)
                        {
                            roles = r;
                        }
                    }

                    break;
            }
        }

        return (policy, roles, anonymous);
    }

    // The compilation itself, plus every referenced assembly that references Rask.Cqrs. Only those can
    // declare a message or a handler, so this skips the BCL and every unrelated package without walking
    // a single namespace of them.
    private static List<IAssemblySymbol> MessageAssemblies(Compilation compilation)
    {
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (reference.Modules.Any(m => m.ReferencedAssemblies.Any(a => a.Name == CqrsAssembly)))
            {
                assemblies.Add(reference);
            }
        }

        return assemblies;
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

            if (contract.Policy is { } policy)
            {
                entry.AppendLine($"            Policy = \"{policy}\",");
            }

            if (contract.Roles is { } roles)
            {
                entry.AppendLine($"            Roles = \"{roles}\",");
            }

            if (contract.AllowAnonymous)
            {
                entry.AppendLine("            AllowAnonymous = true,");
            }

            entry.AppendLine($"            CarriesFiles = {(contract.CarriesFiles ? "true" : "false")},");
            entry.AppendLine($"            ReturnsFile = {(contract.ReturnsFile ? "true" : "false")},");
            // The server's mirror of the invoker below: it runs the message against its local handler and
            // boxes the result, so an endpoint holding the message only as `object` can serialize what
            // comes back. Cast to the message interface rather than the concrete type — a type that
            // implements both ICommand and ICommand<T> would otherwise make the call ambiguous.
            var local = contract.Kind switch
            {
                RemoteKind.Query =>
                    $"(object)await Dispatcher(provider).QueryAsync((global::Rask.Cqrs.IQuery<{contract.ResultFqn}>)message, cancellationToken)",
                RemoteKind.ResultCommand =>
                    $"(object)await Dispatcher(provider).SendAsync((global::Rask.Cqrs.ICommand<{contract.ResultFqn}>)message, cancellationToken)",
                RemoteKind.VoidCommand =>
                    "await Dispatcher(provider).SendAsync((global::Rask.Cqrs.ICommand)message, cancellationToken); return null",
                _ =>
                    $"await Dispatcher(provider).PublishAsync(({contract.Message.Fqn})message, cancellationToken); return null",
            };

            // Emitted only where a handler actually exists, so the endpoint can tell "I cannot serve this"
            // from "I can" without asking the registry - and answer 404 rather than letting the dispatcher
            // throw its no-handler exception into a 500.
            if (contract.HasLocalHandler)
            {
                entry.AppendLine(
                    "            LocalInvoker = static async (provider, message, cancellationToken) => "
                    + $"{{ {(contract.Kind is RemoteKind.VoidCommand or RemoteKind.Notification ? local : "return " + local)}; }},");
            }

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
        // Emitted ONLY when something actually carries a file. The adapter names Rask.Core, and most
        // compilations that reference a transport never send one - emitting it unconditionally would make
        // Rask.Core a hard requirement of remote CQRS for apps that have no use for it.
        if (contracts.Any(c => c.CarriesFiles))
        {
            // The adapter that lets a MESSAGE speak in RaskFile while the wire speaks in RemoteFile. It is
            // emitted into the consumer's compilation deliberately: that is the only assembly that sees both
            // Rask.Core and Rask.Cqrs, so putting it here keeps the mediator standalone and keeps the server
            // transport free of a Rask.Core reference. A handler therefore receives exactly what a component
            // would hand it in-process - the same type, on every host.
            sb.AppendLine(
                "internal sealed class __RaskCqrsUploadedFile : global::Rask.Core.Forms.RaskFile");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly global::Rask.Cqrs.RemoteFile _wire;");
            sb.AppendLine();
            sb.AppendLine("    public __RaskCqrsUploadedFile(global::Rask.Cqrs.RemoteFile wire) { _wire = wire; }");
            sb.AppendLine();
            sb.AppendLine("    public override string Name => _wire.Name;");
            sb.AppendLine();
            sb.AppendLine("    public override long Size => _wire.Size;");
            sb.AppendLine();
            sb.AppendLine("    public override string ContentType => _wire.ContentType;");
            sb.AppendLine();
            sb.AppendLine(
                "    public override global::System.DateTimeOffset LastModified => "
                + "_wire.LastModified ?? global::System.DateTimeOffset.UnixEpoch;");
            sb.AppendLine();
            sb.AppendLine("    // The ceiling is honoured exactly as a browser-backed RaskFile honours it, so a");
            sb.AppendLine("    // handler written against one behaves the same against the other. Size is unknown");
            sb.AppendLine("    // (-1) for a stream whose length the sender never declared, and an unknown size");
            sb.AppendLine("    // cannot be checked against a ceiling - the transport's own cap bounds it instead.");
            sb.AppendLine(
                "    public override global::System.IO.Stream OpenReadStream("
                + "long maxAllowedSize = 512 * 1024, "
                + "global::System.Threading.CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_wire.Size >= 0 && _wire.Size > maxAllowedSize)");
            sb.AppendLine("        {");
            sb.AppendLine(
                "            throw new global::System.IO.IOException($\"File '{_wire.Name}' is {_wire.Size} bytes, "
                + "exceeds maxAllowedSize of {maxAllowedSize}.\");");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return _wire.OpenReadStream(cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("internal static class __RaskCqrsCodecs");
        sb.AppendLine("{");
        sb.AppendLine("    // Results never carry files, so their codecs are handed a list that can be shared and");
        sb.AppendLine("    // never written to.");
        sb.AppendLine("    private static readonly global::Rask.Cqrs.RemoteFile[] NoFiles = new global::Rask.Cqrs.RemoteFile[0];");
        sb.AppendLine();
        sb.Append(emitter.Methods);

        sb.AppendLine("    private static global::Rask.Cqrs.IDispatcher Dispatcher(global::System.IServiceProvider provider) =>");
        sb.AppendLine("        provider.GetService(typeof(global::Rask.Cqrs.IDispatcher)) as global::Rask.Cqrs.IDispatcher");
        sb.AppendLine("        ?? throw new global::System.InvalidOperationException(");
        sb.AppendLine("            \"Rask.Cqrs is not registered in this scope. Call AddRaskCqrsServer() during startup.\");");
        sb.AppendLine();
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

        public string? Policy { get; set; }

        public string? Roles { get; set; }

        public bool AllowAnonymous { get; set; }

        public bool HasLocalHandler { get; set; }

        public string? Problem { get; set; }

        public static ContractModel Failed(string problem) => new() { Problem = problem };
    }
}
