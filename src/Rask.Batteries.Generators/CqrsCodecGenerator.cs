using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

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
///         implement, are excluded: that is how <c>IJob</c> keeps a whole family
///         of always-in-process messages out of the wire vocabulary.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CqrsCodecGenerator : IIncrementalGenerator
{
    private const string CqrsNamespace = "Rask.Cqrs";
    private const string CqrsAssembly = "Rask.Cqrs";
    private const string WireNamespace = "Rask.Wire";

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
                     + "sent anywhere — a job payload, an event only this process handles, a command only another handler publishes — "
                     + "should say so with [LocalOnly], which exempts it entirely.",
        helpLinkUri: DiagnosticHelp.Link("RASK053"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Off only when the build says so: Rask.Server sets it from whether the project builds a browser client
        // (Client/), because it references the server transport for Serve() and a server-rendered app — whose
        // messages never leave the process — should neither pay for the codec nor meet RASK053. A project that
        // does not come through Rask.Server never sets it, and keeps the codec as before.
        var codecOff = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.RaskCqrsCodec", out var value)
            && value.Equals("false", System.StringComparison.OrdinalIgnoreCase));

        // Deliberately driven straight off the compilation rather than through a cached model: the
        // discovery walk reaches into referenced assemblies, and the symbols it produces must not be
        // held across an incremental-pipeline boundary. Everything is done inside the output callback,
        // so nothing outlives the compilation it came from.
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(codecOff),
            static (spc, pair) =>
            {
                if (!pair.Right)
                {
                    Execute(spc, pair.Left);
                }
            });
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation)
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
                ApplyAuthorization(model, message.Type, message.Kind, handlers);
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
    }

    private static void ApplyAuthorization(
        ContractModel model, INamedTypeSymbol type, RemoteKind kind, Dictionary<string, INamedTypeSymbol> handlers)
    {
        handlers.TryGetValue(type.ToDisplayString(), out var handler);
        model.HasLocalHandler = handler is not null;
        var authorization = Authorization(handler);
        model.Policies = authorization.Policies;
        model.RoleSets = authorization.RoleSets;
        model.AllowAnonymous = authorization.AllowAnonymous;
        model.RequiresAuthentication = authorization.Authorize;

        // Who may SUBSCRIBE is the record's own business, so it is read off the record rather than a
        // handler — neither an event nor a subscription has one. An event that declares nothing
        // stays closed to bare subscribers: every auth event would otherwise be one browser request away. A
        // subscription record needs no declaration, since its IWatchPolicy already fails closed, but honours
        // one when it carries it.
        if (kind is RemoteKind.Event or RemoteKind.Subscription && HasAuthorization(type))
        {
            var subscribe = Authorization(type);
            model.SubscribeDeclared = true;
            model.SubscribePolicies = subscribe.Policies;
            model.SubscribeRoleSets = subscribe.RoleSets;
            model.SubscribeAnonymously = subscribe.AllowAnonymous;
        }
    }

    // A transport is what makes wire encoding meaningful. Without one, generating codecs would impose
    // the contract shape rules on an app that never sends anything anywhere.
    private static bool ReferencesTransport(Compilation compilation) =>
        compilation.SourceModule.ReferencedAssemblySymbols
            .SelectMany(static reference => reference.GetAttributes())
            .Any(static attribute =>
                attribute.AttributeClass?.Name is "RaskCqrsTransportAttribute" &&
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() is CqrsNamespace);

    private static IEnumerable<(INamedTypeSymbol Type, RemoteKind Kind, ITypeSymbol? ResultType)> DiscoverMessages(
        Compilation compilation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in MessageAssemblies(compilation))
        {
            foreach (var type in SymbolWalk.AllTypes(assembly.GlobalNamespace))
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

                var kind = RemoteKindOf(type, out var resultType);
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
        var map = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var assembly in MessageAssemblies(compilation))
        {
            foreach (var type in SymbolWalk.AllTypes(assembly.GlobalNamespace))
            {
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                {
                    continue;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (iface.ContainingNamespace?.ToDisplayString() is not CqrsNamespace || !iface.IsGenericType)
                    {
                        continue;
                    }

                    var handles = iface.MetadataName is "IQueryHandler`2" or "ICommandHandler`1"
                        or "ICommandHandler`2" or "IEventHandler`1";

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
    //
    // EVERY attribute is kept, the type's own and its base types': ASP.NET requires all of them to pass, and
    // a page does (RouteAuthorizationGuard). Keeping only the last one checked [Authorize(Policy = "billing")]
    // and quietly dropped the [Authorize(Policy = "members")] above it (#1178).
    private static (List<string> Policies, List<string> RoleSets, bool AllowAnonymous, bool Authorize) Authorization(
        INamedTypeSymbol? handler)
    {
        var policies = new List<string>();
        var roleSets = new List<string>();
        var anonymous = false;
        var authorize = false;

        foreach (var attribute in DeclaredAndInherited(handler))
        {
            switch (attribute.AttributeClass?.Name)
            {
                case "AllowAnonymousAttribute":
                    anonymous = true;
                    break;

                case "AuthorizeAttribute":
                    authorize = true;
                    if (attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is string { Length: > 0 } positional)
                    {
                        policies.Add(positional);
                    }

                    foreach (var named in attribute.NamedArguments)
                    {
                        if (named.Key is "Policy" && named.Value.Value is string { Length: > 0 } p)
                        {
                            policies.Add(p);
                        }
                        else if (named.Key is "Roles" && named.Value.Value is string { Length: > 0 } r)
                        {
                            roleSets.Add(r);
                        }
                    }

                    break;
            }
        }

        return (policies, roleSets, anonymous, authorize);
    }

    private static IEnumerable<AttributeData> DeclaredAndInherited(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                yield return attribute;
            }
        }
    }

    private static bool HasAuthorization(INamedTypeSymbol type) =>
        DeclaredAndInherited(type).Any(a => a.AttributeClass?.Name is "AuthorizeAttribute" or "AllowAnonymousAttribute");

    // A policy or role name is whatever the author typed, so it is written as a literal rather than
    // between two quotes: a name holding a quote or a backslash would otherwise end the string early.
    private static string Literals(List<string> values) =>
        "[" + string.Join(", ", values.Select(v => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(v, quote: true))) + "]";

    // The compilation itself, plus every referenced assembly that references Rask.Cqrs. Only those can
    // declare a message or a handler, so this skips the BCL and every unrelated package without walking
    // a single namespace of them.
    private static List<IAssemblySymbol> MessageAssemblies(Compilation compilation)
    {
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        assemblies.AddRange(compilation.SourceModule.ReferencedAssemblySymbols
            .Where(reference => reference.Modules.Any(m => m.ReferencedAssemblies.Any(a => a.Name is CqrsAssembly))));
        return assemblies;
    }

    // [LocalOnly] on the message, or on any interface it implements. The interface form is what lets one
    // line in Rask.Jobs keep every job payload in-process.
    private static bool IsLocalOnly(INamedTypeSymbol type)
    {
        if (HasLocalOnly(type) || type.AllInterfaces.Any(HasLocalOnly))
        {
            return true;
        }

        return false;
    }

    private static bool HasLocalOnly(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "LocalOnlyAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() is CqrsNamespace);

    private static RemoteKind? RemoteKindOf(INamedTypeSymbol type, out ITypeSymbol? resultType)
    {
        resultType = null;
        RemoteKind? kind = null;

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.ContainingNamespace?.ToDisplayString() is not CqrsNamespace)
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
                // both IEvent and ICommand, and the more specific shape should win.
                case "ICommand":
                    kind ??= RemoteKind.VoidCommand;
                    break;

                case "IEvent":
                    kind ??= RemoteKind.Event;
                    break;

                // A subscription is never anything else, so it wins outright: what it carries is its result.
                case "ISubscription`1":
                    resultType = @interface.TypeArguments[0];
                    return RemoteKind.Subscription;
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
        // The compilation lets a Rask.Data entity's generated model — which this generator cannot see — be
        // encoded from the entity rather than rejected as a type with no way to build it.
        var message = WireShape.Classify(type, allowFile: true, compilation: compilation);
        if (message.Kind == WireKind.Unsupported)
        {
            return ContractModel.Failed(message.Reason!);
        }

        var returnsFile = resultType is not null && IsFileDownload(resultType);
        WireType? result = null;
        if (resultType is not null && !returnsFile)
        {
            result = WireShape.Classify(resultType, allowFile: false, compilation: compilation);
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
            ResultFqn = (returnsFile, resultType) switch
            {
                (true, _) => "global::Rask.Wire.FileDownload",
                (_, null) => "global::Rask.Cqrs.Unit",
                (_, { } answer) => GeneratedModelShape.DisplayName(answer, SymbolDisplayFormat.FullyQualifiedFormat, compilation),
            },
            ReturnsFile = returnsFile,
            CarriesFiles = message.ContainsFile,
            WireName = type.ToDisplayString(),
        };
    }

    // FileDownload lives in Rask.Wire, not Rask.Cqrs: it is a wire carrier, and Rask.Api's codecs need
    // the same one. Matched by name and namespace rather than by symbol identity so this generator keeps
    // no reference to either package.
    private static bool IsFileDownload(ITypeSymbol type) =>
        type.Name is "FileDownload" && type.ContainingNamespace?.ToDisplayString() is WireNamespace;

    private static string Build(List<ContractModel> contracts)
    {
        var emitter = new WireCodecEmitter();
        var registrations = new List<(string Field, string Declaration)>();

        foreach (var contract in contracts.OrderBy(c => c.WireName, System.StringComparer.Ordinal))
        {
            var messageId = emitter.Ensure(contract.Message);
            var resultId = contract.Result is null ? null : emitter.Ensure(contract.Result);

            var field = "C" + registrations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            registrations.Add((field, Registration(contract, field, messageId, resultId)));
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
            AppendUploadedFileAdapter(sb);
        }

        sb.AppendLine("internal static class __RaskCqrsCodecs");
        sb.AppendLine("{");
        sb.AppendLine("    // Results never carry files, so their codecs are handed a list that can be shared and");
        sb.AppendLine("    // never written to.");
        sb.AppendLine("    private static readonly global::Rask.Wire.RemoteFile[] NoFiles = new global::Rask.Wire.RemoteFile[0];");
        sb.AppendLine();
        sb.Append(emitter.Methods);

        AppendServiceLookups(sb);

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

    private static string Registration(ContractModel contract, string field, string messageId, string? resultId)
    {
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

        AppendAuthorization(entry, contract);

        entry.AppendLine($"            CarriesFiles = {(contract.CarriesFiles ? "true" : "false")},");
        entry.AppendLine($"            ReturnsFile = {(contract.ReturnsFile ? "true" : "false")},");
        AppendInvokers(entry, contract, field);

        entry.AppendLine("        };");
        entry.AppendLine();
        return entry.ToString();
    }

    private static void AppendAuthorization(StringBuilder entry, ContractModel contract)
    {
        if (contract.Policies.Count > 0)
        {
            entry.AppendLine($"            Policies = {Literals(contract.Policies)},");
        }

        if (contract.RoleSets.Count > 0)
        {
            entry.AppendLine($"            RoleSets = {Literals(contract.RoleSets)},");
        }

        if (contract.AllowAnonymous)
        {
            entry.AppendLine("            AllowAnonymous = true,");
        }

        if (contract.RequiresAuthentication)
        {
            entry.AppendLine("            RequiresAuthentication = true,");
        }

        if (contract.SubscribeDeclared)
        {
            entry.AppendLine("            SubscribeDeclared = true,");
            if (contract.SubscribePolicies.Count > 0)
            {
                entry.AppendLine($"            SubscribePolicies = {Literals(contract.SubscribePolicies)},");
            }

            if (contract.SubscribeRoleSets.Count > 0)
            {
                entry.AppendLine($"            SubscribeRoleSets = {Literals(contract.SubscribeRoleSets)},");
            }

            if (contract.SubscribeAnonymously)
            {
                entry.AppendLine("            SubscribeAnonymously = true,");
            }
        }
    }

    private static void AppendInvokers(StringBuilder entry, ContractModel contract, string field)
    {
        // The server's mirror of the invoker below: it runs the message against its local handler and
        // boxes the result, so an endpoint holding the message only as `object` can serialize what
        // comes back. Cast to the message interface rather than the concrete type — a type that
        // implements both ICommand and ICommand<T> would otherwise make the call ambiguous.
        var local = contract.Kind switch
        {
            RemoteKind.Query =>
                $"(object)await Dispatcher(provider).Query((global::Rask.Cqrs.IQuery<{contract.ResultFqn}>)message, cancellationToken)",
            RemoteKind.ResultCommand =>
                $"(object)await Dispatcher(provider).Send((global::Rask.Cqrs.ICommand<{contract.ResultFqn}>)message, cancellationToken)",
            RemoteKind.VoidCommand =>
                "await Dispatcher(provider).Send((global::Rask.Cqrs.ICommand)message, cancellationToken); return null",
            _ =>
                $"await Dispatcher(provider).Publish(({contract.Message.Fqn})message, cancellationToken); return null",
        };

        // Emitted only where a handler actually exists, so the endpoint can tell "I cannot serve this"
        // from "I can" without asking the registry - and answer 404 rather than letting the dispatcher
        // throw its no-handler exception into a 500.
        if (contract.HasLocalHandler && contract.Kind != RemoteKind.Subscription)
        {
            entry.AppendLine(
                "            LocalInvoker = static async (provider, message, cancellationToken) => "
                + $"{{ {(contract.Kind is RemoteKind.VoidCommand or RemoteKind.Event ? local : "return " + local)}; }},");
        }

        // A request's invoker is emitted closed over the concrete result type, which is what lets a
        // client hand back a real Task<TResult> without MakeGenericType. Events need none:
        // IRemoteDispatch.PublishAsync is not generic, so a transport calls it directly.
        if (contract.Kind is not (RemoteKind.Event or RemoteKind.Subscription))
        {
            var send = contract.Kind == RemoteKind.VoidCommand
                ? $"Remote(provider).Send({field}, message, cancellationToken)"
                : $"Remote(provider).Send<{contract.ResultFqn}>({field}, message, cancellationToken)";
            entry.AppendLine(
                $"            Invoker = static (provider, message, cancellationToken) => {send},");
        }
    }

    // The adapter that lets a MESSAGE speak in IRaskFile while the wire speaks in RemoteFile. It is
    // emitted into the consumer's compilation deliberately: that is the only assembly that sees both
    // Rask.Core and Rask.Cqrs, so putting it here keeps the mediator standalone and keeps the server
    // transport free of a Rask.Core reference. A handler therefore receives exactly what a component
    // would hand it in-process - the same type, on every host.
    private static void AppendUploadedFileAdapter(StringBuilder sb)
    {
        sb.AppendLine(
            "internal sealed class __RaskCqrsUploadedFile : global::Rask.Core.Forms.IRaskFile");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::Rask.Wire.RemoteFile _wire;");
        sb.AppendLine();
        sb.AppendLine("    public __RaskCqrsUploadedFile(global::Rask.Wire.RemoteFile wire) { _wire = wire; }");
        sb.AppendLine();
        sb.AppendLine("    public string Name => _wire.Name;");
        sb.AppendLine();
        sb.AppendLine("    public long Size => _wire.Size;");
        sb.AppendLine();
        sb.AppendLine("    public string ContentType => _wire.ContentType;");
        sb.AppendLine();
        sb.AppendLine(
            "    public global::System.DateTimeOffset LastModified => "
            + "_wire.LastModified ?? global::System.DateTimeOffset.UnixEpoch;");
        sb.AppendLine();
        sb.AppendLine("    // The ceiling is honoured exactly as a browser-backed IRaskFile honours it, so a");
        sb.AppendLine("    // handler written against one behaves the same against the other. Size is unknown");
        sb.AppendLine("    // (-1) for a stream whose length the sender never declared, and an unknown size");
        sb.AppendLine("    // cannot be checked against a ceiling - the transport's own cap bounds it instead.");
        sb.AppendLine(
            "    public global::System.IO.Stream OpenReadStream("
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

    private static void AppendServiceLookups(StringBuilder sb)
    {
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
    }

    private const string Reader = "global::System.Text.Json.Utf8JsonReader";
    private const string FileListRead = "global::System.Collections.Generic.IReadOnlyList<global::Rask.Wire.RemoteFile>";

    private enum RemoteKind
    {
        Query,
        VoidCommand,
        ResultCommand,
        Event,
        Subscription,
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

        public List<string> Policies { get; set; } = new();

        public List<string> RoleSets { get; set; } = new();

        public bool AllowAnonymous { get; set; }

        public bool RequiresAuthentication { get; set; }

        public bool HasLocalHandler { get; set; }

        public bool SubscribeDeclared { get; set; }

        public List<string> SubscribePolicies { get; set; } = new();

        public List<string> SubscribeRoleSets { get; set; } = new();

        public bool SubscribeAnonymously { get; set; }

        public string? Problem { get; set; }

        public static ContractModel Failed(string problem) => new() { Problem = problem };
    }
}
