using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Api.Generators;

/// <summary>
///     Emits a typed client per API controller, so a call site carries no URL string.
/// </summary>
/// <remarks>
///     <para>
///         The server declaration is the source of truth: an ordinary <c>[ApiController]</c> is read for
///         its routes, verbs, parameters and response types, and the client is generated from that. There
///         is no second declaration to keep in step, which is the whole point — a route renamed on the
///         server breaks the call site at compile time instead of at 404 time.
///     </para>
///     <para>
///         Reflection-free throughout: the JSON codecs come from <see cref="WireCodecEmitter" />, the same
///         one Rask.Cqrs' wire uses, so a shape means the same thing on either.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ApiClientGenerator : IIncrementalGenerator
{
    internal const string BakedProperty = "build_property.RaskApiClientBaked";

    private const string MvcNamespace = "Microsoft.AspNetCore.Mvc";

    private static readonly DiagnosticDescriptor NoWireEncoding = new(
        "RASK067",
        "Endpoint shape has no wire encoding",
        "Endpoint '{0}' cannot be called from a generated client: {1}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelp.Link("RASK067"));

    private static readonly DiagnosticDescriptor EndpointSkipped = new(
        "RASK068",
        "Endpoint has no generated client method",
        "Endpoint '{0}' gets no typed client method: {1}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelp.Link("RASK068"));

    private static readonly DiagnosticDescriptor DuplicateClientMethod = new(
        "RASK069",
        "Two endpoints claim one client method",
        "Endpoints '{0}' and '{1}' both generate '{2}'. Rename one action, or give one an explicit name.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelp.Link("RASK069"));

    private static readonly DiagnosticDescriptor UntypedResult = new(
        "RASK070",
        "Endpoint's response type is not statically known",
        "Endpoint '{0}' returns '{1}', so its response type is not known at compile time and it gets no "
        + "typed client method. Return T, Task<T> or ActionResult<T>, or declare "
        + "[ProducesResponseType(typeof(T), 200)].",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: DiagnosticHelp.Link("RASK070"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The bake flag is read through its own provider so it stays out of the compilation cache key,
        // the same shape CqrsCodecGenerator uses for RaskEmitTypeScript.
        var baked = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue(BakedProperty, out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase));

        var controllers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MvcNamespace + ".ApiControllerAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var minimal = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => MinimalApi.IsCandidate(node),
                static (ctx, _) => MinimalApi.Read(ctx.SemanticModel, (InvocationExpressionSyntax)ctx.Node))
            .Where(static registration => registration is not null)
            .Collect();

        var assembly = context.CompilationProvider.Select(
            static (compilation, _) => compilation.AssemblyName ?? "RaskGeneratedClients");

        context.RegisterSourceOutput(
            controllers.Combine(minimal).Combine(assembly).Combine(baked),
            static (spc, input) =>
            {
                var (((types, registrations), assemblyName), isBaked) = input;

                // The browser companion compiles the client that was baked out of the server assembly.
                // If this generator also emitted one there, every client type would be declared twice
                // (CS0101).
                if (isBaked)
                {
                    return;
                }

                Emit(spc, types, registrations, assemblyName);
            });
    }

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<INamedTypeSymbol> controllers,
        ImmutableArray<MinimalApiRegistration?> registrations,
        string assemblyName)
    {
        var endpoints = new List<ApiEndpoint>();

        if (!controllers.IsDefaultOrEmpty)
        {
            foreach (var controller in controllers.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>())
            {
                if (!DerivesFromControllerBase(controller))
                {
                    continue;
                }

                foreach (var endpoint in Describe(spc, controller))
                {
                    endpoints.Add(endpoint);
                }
            }
        }

        if (!registrations.IsDefaultOrEmpty)
        {
            foreach (var registration in registrations)
            {
                var endpoint = Describe(spc, registration!, assemblyName);

                if (endpoint is not null)
                {
                    endpoints.Add(endpoint);
                }
            }
        }

        if (endpoints.Count == 0)
        {
            return;
        }

        // A duplicate would otherwise be a confusing CS0111 inside generated code. Reported here, where
        // the message can name both actions.
        var seen = new Dictionary<string, ApiEndpoint>(StringComparer.Ordinal);
        var emitted = new List<ApiEndpoint>();

        foreach (var endpoint in endpoints)
        {
            var key = endpoint.ClientNamespace + "." + endpoint.ClientName + "." + endpoint.MethodName;

            if (seen.TryGetValue(key, out var existing))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DuplicateClientMethod, Location.None, existing.DeclaredBy, endpoint.DeclaredBy, key));
                continue;
            }

            seen.Add(key, endpoint);
            emitted.Add(endpoint);
        }

        spc.AddSource("__RaskApiClients.g.cs", Build(emitted));
    }

    private static bool DerivesFromControllerBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "ControllerBase" &&
                current.ContainingNamespace?.ToDisplayString() == MvcNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ApiEndpoint> Describe(SourceProductionContext spc, INamedTypeSymbol controller)
    {
        var prefix = RouteTemplate.OfController(controller);
        var clientName = ClientNameOf(controller);
        var clientNamespace = controller.ContainingNamespace.IsGlobalNamespace
            ? "RaskGeneratedClients"
            : controller.ContainingNamespace.ToDisplayString();

        foreach (var member in controller.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary ||
                member.DeclaredAccessibility != Accessibility.Public ||
                member.IsStatic ||
                HasAttribute(member, "NonActionAttribute"))
            {
                continue;
            }

            foreach (var (verb, template) in RouteTemplate.OfAction(member))
            {
                var endpoint = Build(spc, controller, member, prefix, template, verb, clientName, clientNamespace);

                if (endpoint is not null)
                {
                    yield return endpoint;
                }
            }
        }
    }

    /// <summary>Reads one minimal API registration into the same model a controller action produces.</summary>
    private static ApiEndpoint? Describe(
        SourceProductionContext spc,
        MinimalApiRegistration registration,
        string assemblyName)
    {
        var where = registration.Pattern.Length == 0
            ? registration.Verb + " (route not a constant)"
            : registration.Verb + " " + registration.Pattern;

        if (registration.Pattern == MinimalApi.GroupedMarker)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                EndpointSkipped, registration.Site, registration.Verb + " (in a MapGroup)",
                "it is mapped on a MapGroup, whose prefix is not visible from this call. The client "
                + "would build the route without it and address an endpoint the server does not answer. "
                + "Map it on the app with its full route, or call it by hand"));
            return null;
        }

        if (registration.Pattern.Length == 0)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                EndpointSkipped, registration.Site, where,
                "its route pattern is not a compile-time constant, so no client method can name it"));
            return null;
        }

        if (registration.Handler is null)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                EndpointSkipped, registration.Site, where,
                "its handler's parameter types are not declared. Give the lambda typed parameters, or "
                + "pass a method group"));
            return null;
        }

        var route = registration.Pattern.StartsWith("/", StringComparison.Ordinal)
            ? registration.Pattern
            : "/" + registration.Pattern;

        return Compose(
            spc,
            registration.Handler,
            route,
            registration.Verb,
            MinimalApi.ClientName(route),
            assemblyName,
            registration.Name ?? MinimalApi.MethodName(registration.Verb, route),
            where,
            registration.Site);
    }

    private static ApiEndpoint? Build(
        SourceProductionContext spc,
        INamedTypeSymbol controller,
        IMethodSymbol action,
        string prefix,
        string template,
        string verb,
        string clientName,
        string clientNamespace)
    {
        var declaredBy = controller.Name + "." + action.Name;
        var route = RouteTemplate.Combine(prefix, template, controller, action);

        return Compose(
            spc, action, route, verb, clientName, clientNamespace, action.Name, declaredBy, At(action));
    }

    /// <summary>
    ///     Turns a handler and a resolved route into a client method, or reports why it cannot.
    /// </summary>
    /// <remarks>
    ///     Shared by both front doors on purpose. A controller action and a minimal API lambda bind their
    ///     parameters by nearly the same rules, and two copies of that logic would drift — with the drift
    ///     showing up as a wrong URL rather than as a build error.
    /// </remarks>
    private static ApiEndpoint? Compose(
        SourceProductionContext spc,
        IMethodSymbol action,
        string route,
        string verb,
        string clientName,
        string clientNamespace,
        string methodName,
        string declaredBy,
        Location site)
    {

        if (route.Contains("{*"))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                EndpointSkipped, site, declaredBy,
                "its route has a catch-all segment, which a typed client cannot fill from a parameter"));
            return null;
        }

        var tokens = RouteTemplate.Tokens(route);

        if (!TryResult(spc, action, declaredBy, out var resultType, out var resultFqn))
        {
            return null;
        }

        var parameters = new List<ApiParameter>();
        var bodySeen = false;

        foreach (var parameter in action.Parameters)
        {
            if (IsInfrastructure(parameter))
            {
                continue;
            }

            var shape = WireShape.Classify(parameter.Type, allowFile: false);

            if (shape.Kind == WireKind.Unsupported)
            {
                // An interface, an abstract type or an open generic is exactly the shape of an injected
                // service, so an unattributed one is skipped rather than reported: [FromServices] is
                // optional on a controller action, and demanding it would reject correct code.
                if (BindingAttribute(parameter) is null)
                {
                    continue;
                }

                spc.ReportDiagnostic(Diagnostic.Create(
                    NoWireEncoding, At(parameter), declaredBy,
                    $"parameter '{parameter.Name}' {shape.Reason}"));
                return null;
            }

            var binding = BindingOf(parameter, shape, tokens, ref bodySeen);

            if (binding is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    EndpointSkipped, site, declaredBy,
                    $"parameter '{parameter.Name}' would be a second request body, and a request has one"));
                return null;
            }

            // Refused HERE, where it can be reported. The emitter has no way to attach a header to the
            // request, and a comment further down once claimed this case never reached it — it did, and
            // the emitter threw, which fails the whole generator with CS8785 and leaves the compilation
            // with no clients at all. One unsupported parameter must cost one endpoint, not all of them.
            if (binding == ApiBinding.Header)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    EndpointSkipped, site, declaredBy,
                    $"parameter '{parameter.Name}' binds from a request header, which a generated client "
                    + "cannot send. Pass it as a route or query value, or set it for every call with "
                    + "ApiClientOptions.ConfigureRequestAsync"));
                return null;
            }

            parameters.Add(new ApiParameter(
                parameter.Name,
                WireNameOf(parameter),
                shape,
                binding.Value,
                parameter.Type.ToDisplayString(Fqn),
                parameter.HasExplicitDefaultValue,
                DefaultLiteral(parameter)));
        }

        // A route token no parameter fills would be substituted with nothing, producing a URL that
        // silently addresses the wrong resource.
        foreach (var token in tokens)
        {
            if (!parameters.Any(p => p.Binding == ApiBinding.Route &&
                    string.Equals(p.WireName, token, StringComparison.OrdinalIgnoreCase)))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    EndpointSkipped, site, declaredBy,
                    $"its route names '{{{token}}}' but no parameter supplies it"));
                return null;
            }
        }

        return new ApiEndpoint(
            verb, route, clientName, clientNamespace, methodName, parameters, resultType, resultFqn, declaredBy);
    }

    private static bool TryResult(
        SourceProductionContext spc,
        IMethodSymbol action,
        string declaredBy,
        out WireType? shape,
        out string? fqn)
    {
        shape = null;
        fqn = null;

        var returned = Unwrap(action.ReturnType);

        // void / Task: the endpoint answers nothing, and the client method returns Task.
        if (returned is null)
        {
            return true;
        }

        // TypedResults — Ok<T>, Results<Ok<T>, NotFound>, NoContent — carry the response type in the
        // signature, which is the whole reason they exist. Reading them is what keeps the idiomatic
        // minimal API style from being reported as untyped.
        if (TryTypedResult(returned, out var body))
        {
            if (body is null)
            {
                return true;
            }

            returned = body;
        }
        else if (IsUntypedResult(returned))
        {
            var declared = ProducesResponseType(action);

            if (declared is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    UntypedResult, At(action), declaredBy, returned.ToDisplayString()));
                return false;
            }

            returned = declared;
        }

        var classified = WireShape.Classify(returned, allowFile: false);

        if (classified.Kind == WireKind.Unsupported)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                NoWireEncoding, At(action), declaredBy,
                $"its response type '{returned.ToDisplayString()}' {classified.Reason}"));
            return false;
        }

        shape = classified;
        fqn = returned.ToDisplayString(Fqn);
        return true;
    }

    // Task<T> / ValueTask<T> / ActionResult<T> peel away in any combination; null means "answers nothing".
    private static ITypeSymbol? Unwrap(ITypeSymbol type)
    {
        while (true)
        {
            if (type.SpecialType == SpecialType.System_Void)
            {
                return null;
            }

            if (type is not INamedTypeSymbol named)
            {
                return type;
            }

            if (named.TypeArguments.Length == 0)
            {
                return named.Name is "Task" or "ValueTask" ? null : named;
            }

            if (named.Name is "Task" or "ValueTask" or "ActionResult")
            {
                type = named.TypeArguments[0];
                continue;
            }

            return named;
        }
    }

    /// <summary>
    ///     Reads a <c>Microsoft.AspNetCore.Http.HttpResults</c> type: true when it is one, with
    ///     <paramref name="body" /> set to what it answers with, or null when it answers with nothing.
    /// </summary>
    /// <remarks>
    ///     <c>Results&lt;Ok&lt;Post&gt;, NotFound&gt;</c> is the shape an author reaches for when they
    ///     want both a typed body and a real 404, so the first alternative carrying a body wins and the
    ///     empty ones are ignored. Without this, the entire <c>TypedResults</c> style — the one Microsoft
    ///     recommends for minimal APIs — would report as having no statically known response type.
    /// </remarks>
    private static bool TryTypedResult(ITypeSymbol type, out ITypeSymbol? body)
    {
        body = null;

        if (type.ContainingNamespace?.ToDisplayString() != "Microsoft.AspNetCore.Http.HttpResults" ||
            type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.Name == "Results" && named.TypeArguments.Length > 0)
        {
            var recognised = false;

            foreach (var alternative in named.TypeArguments)
            {
                if (!TryTypedResult(alternative, out var candidate))
                {
                    continue;
                }

                recognised = true;

                if (candidate is not null && body is null)
                {
                    body = candidate;
                }
            }

            return recognised;
        }

        // The carriers that hold a value. Anything else in the namespace — NotFound, NoContent,
        // BadRequest, Unauthorized — is recognised as answering with nothing.
        if (named.TypeArguments.Length == 1 &&
            named.Name is "Ok" or "Created" or "CreatedAtRoute" or "Accepted" or "AcceptedAtRoute"
                or "JsonHttpResult" or "Conflict" or "BadRequest" or "NotFound" or "UnprocessableEntity")
        {
            body = named.TypeArguments[0];
        }

        return true;
    }

    private static bool IsUntypedResult(ITypeSymbol type) =>
        type.Name is "IActionResult" or "ActionResult" or "IResult"
        && type.ContainingNamespace?.ToDisplayString() is MvcNamespace or "Microsoft.AspNetCore.Http";

    private static ITypeSymbol? ProducesResponseType(IMethodSymbol action)
    {
        foreach (var attribute in action.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "ProducesResponseTypeAttribute")
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol type)
                {
                    return type;
                }
            }
        }

        return null;
    }

    // Everything ASP.NET fills in from the request context rather than from the caller. Matched by name
    // so this generator needs no reference to ASP.NET.
    private static bool IsInfrastructure(IParameterSymbol parameter)
    {
        if (HasAttribute(parameter, "FromServicesAttribute") ||
            HasAttribute(parameter, "FromKeyedServicesAttribute"))
        {
            return true;
        }

        return parameter.Type.Name switch
        {
            "HttpContext" or "HttpRequest" or "HttpResponse" or "CancellationToken" or "ClaimsPrincipal"
                or "IFormFile" or "IFormFileCollection" or "IFormCollection" or "Stream" or "PipeReader" => true,
            _ => false,
        };
    }

    private static ApiBinding? BindingOf(
        IParameterSymbol parameter,
        WireType shape,
        IReadOnlyList<string> tokens,
        ref bool bodySeen)
    {
        switch (BindingAttribute(parameter))
        {
            case "FromRouteAttribute":
                return ApiBinding.Route;
            case "FromQueryAttribute":
                return ApiBinding.Query;
            case "FromHeaderAttribute":
                return ApiBinding.Header;
            case "FromBodyAttribute" when bodySeen:
                return null;
            case "FromBodyAttribute":
                bodySeen = true;
                return ApiBinding.Body;
        }

        // The [ApiController] inference rules, in ASP.NET's own order: a name matching a route token
        // binds from the route, a simple type from the query, and the one complex type left is the body.
        if (tokens.Any(t => string.Equals(t, parameter.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return ApiBinding.Route;
        }

        if (IsSimple(shape))
        {
            return ApiBinding.Query;
        }

        if (bodySeen)
        {
            return null;
        }

        bodySeen = true;
        return ApiBinding.Body;
    }

    private static bool IsSimple(WireType shape) => shape.Kind switch
    {
        WireKind.Scalar or WireKind.Enum => true,
        WireKind.Nullable => shape.Inner is null || IsSimple(shape.Inner),
        WireKind.Sequence => shape.Inner is not null && IsSimple(shape.Inner),
        _ => false,
    };

    private static string? BindingAttribute(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;

            if (name is "FromRouteAttribute" or "FromQueryAttribute" or "FromBodyAttribute"
                or "FromHeaderAttribute" &&
                attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() == MvcNamespace)
            {
                return name;
            }
        }

        return null;
    }

    private static string WireNameOf(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is not ("FromRouteAttribute" or "FromQueryAttribute"
                or "FromHeaderAttribute"))
            {
                continue;
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Name" && named.Value.Value is string name && name.Length > 0)
                {
                    return name;
                }
            }
        }

        return parameter.Name;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == attributeName)
            {
                return true;
            }
        }

        return false;
    }

    // The action's own default, as a C# literal. Emitting `default` instead would make the client send a
    // zero or a null whenever the caller omits the argument, quietly replacing the server's default with
    // a different value that type-checks on both sides.
    private static string DefaultLiteral(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return "default";
        }

        return parameter.ExplicitDefaultValue switch
        {
            null => "default",
            bool flag => flag ? "true" : "false",
            string text => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true),
            char character => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(character, quote: true),
            // Cast to the declared type so an enum member, a byte or a long keeps its type rather than
            // arriving as an int the compiler then refuses.
            var value => "(" + parameter.Type.ToDisplayString(Fqn) + ")("
                + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) + ")",
        };
    }

    private static string ClientNameOf(INamedTypeSymbol controller) =>
        controller.Name.EndsWith("Controller", StringComparison.Ordinal) && controller.Name.Length > 10
            ? controller.Name.Substring(0, controller.Name.Length - "Controller".Length) + "Client"
            : controller.Name + "Client";

    private static Location At(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;

    private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string Build(IReadOnlyList<ApiEndpoint> endpoints) =>
        ClientEmitter.Emit(endpoints);
}
