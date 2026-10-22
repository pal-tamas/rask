using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Generators.Authorization;

/// <summary>Finds the calls to a component method that carries <c>[Authorize]</c>.</summary>
internal static class GuardedCalls
{
    private static readonly SymbolDisplayFormat Signature = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The call <paramref name="context" /> holds when it reaches a guarded component method; null otherwise.</summary>
    public static GuardedCall? Of(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        var call = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(call, cancellation).Symbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method
            || !Guarded(method) || !IsComponent(method.ContainingType))
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var unsupported = Unsupported(call, method, context.SemanticModel, cancellation);
        var scope = unsupported is null ? Scope(method, compilation, cancellation, out unsupported) : null;
#pragma warning disable RSEXPERIMENTAL002 // the interceptable-location API is the documented way to name a call to intercept
        var intercepts = unsupported is null
            ? context.SemanticModel.GetInterceptableLocation(call, cancellation)?.GetInterceptsLocationAttributeSyntax()
            : null;
#pragma warning restore RSEXPERIMENTAL002
        var line = call.GetLocation().GetLineSpan();
        return new GuardedCall(
            intercepts,
            method.ContainingType.ToDisplayString() + "." + method.Name
            + "(" + string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString())) + ")",
            method.ContainingType.ToDisplayString(Signature),
            method.Name,
            method.ReturnsVoid ? "void" : GeneratedModelShape.DisplayName(method.ReturnType, Signature, compilation),
            Returns(method),
            Awaits(method),
            method.IsStatic,
            new EquatableArray<GuardedParameter>(method.Parameters.Select(p => Parameter(p, compilation))),
            unsupported ?? (intercepts is null ? "the compiler gives this call no location to intercept" : null),
            scope,
            line.Path,
            call.Span,
            line.Span);
    }

    // Any [Authorize] — or an attribute of the app's own that is one — and no [AllowAnonymous] beside it.
    private static bool Guarded(IMethodSymbol method)
    {
        var guarded = false;
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is not { } type)
            {
                continue;
            }

            if (Implements(type, "IAllowAnonymous"))
            {
                return false;
            }

            guarded |= Implements(type, "IAuthorizeData");
        }

        return guarded;
    }

    private static bool Implements(INamedTypeSymbol type, string name) =>
        type.AllInterfaces.Any(i => string.Equals(i.Name, name, StringComparison.Ordinal)
               && string.Equals(i.ContainingNamespace.ToDisplayString(), "Microsoft.AspNetCore.Authorization", StringComparison.Ordinal));

    private static bool IsComponent(INamedTypeSymbol? type)
    {
        for (var at = type; at is not null; at = at.BaseType)
        {
            if (string.Equals(at.Name, "Component", StringComparison.Ordinal)
                && string.Equals(at.ContainingNamespace.ToDisplayString(), "Rask.Core", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // What keeps a call from being checked where it is written. The method group path still checks the method
    // itself, so these only reach a call made directly — which the warning says.
    private static string? Unsupported(
        InvocationExpressionSyntax call, IMethodSymbol method, SemanticModel model, CancellationToken cancellation)
    {
        if (method.IsGenericMethod || method.ContainingType.IsGenericType)
        {
            return "it is generic";
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            return "it returns by reference";
        }

        if (call.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax })
        {
            return "it is called through base";
        }

        var assembly = model.Compilation.Assembly;
        if (!Reachable(method.ContainingType, assembly) || (!method.ReturnsVoid && !Reachable(method.ReturnType, assembly))
            || method.Parameters.Any(p => !Reachable(p.Type, assembly)))
        {
            return "its type or a type in its signature is private";
        }

        return InExpressionTree(call, model, cancellation) ? "it is called inside an expression tree" : null;
    }

    // A type another generator makes is unseen here and prints as the bare name its author wrote. A Rask model or
    // read face is named from the root; any other is named inside the method's own lookup, rebuilt — or, when that
    // cannot be rebuilt exactly, the call is left unintercepted and reported rather than written uncompilable.
    private static EquatableArray<string>? Scope(
        IMethodSymbol method, Compilation compilation, CancellationToken cancellation, out string? unsupported)
    {
        unsupported = null;
        var signature = method.Parameters.Select(p => (Type: p.Type, TypeOf: true))
            .Append((Type: method.ReturnType, TypeOf: false))
            .ToList();
        if (!signature.Any(t => DeclarationScope.NamesUnseen(t.Type, compilation)))
        {
            return null;
        }

        var scope = signature.All(t => DeclarationScope.Nameable(t.Type, compilation, t.TypeOf))
                    && method.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration
            ? DeclarationScope.Of(declaration.GetSyntax(cancellation), compilation, cancellation)
            : null;
        if (scope is null)
        {
            unsupported = "a type in its signature is made by another generator and cannot be named from generated code";
        }

        return scope;
    }

    // Whether a file-local class in this assembly can name the type: the interceptor repeats the signature.
    private static bool Reachable(ITypeSymbol type, IAssemblySymbol assembly)
    {
        for (ISymbol? at = type; at is INamedTypeSymbol named; at = named.ContainingType)
        {
            if (named.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }

            if (named.DeclaredAccessibility == Accessibility.Internal
                && !assembly.Equals(named.ContainingAssembly, SymbolEqualityComparer.Default)
                && !named.ContainingAssembly.GivesAccessTo(assembly))
            {
                return false;
            }

            if (named.TypeArguments.Any(t => !Reachable(t, assembly)))
            {
                return false;
            }
        }

        return type is not IArrayTypeSymbol array || Reachable(array.ElementType, assembly);
    }

    private static bool InExpressionTree(SyntaxNode call, SemanticModel model, CancellationToken cancellation) =>
        call.Ancestors().OfType<LambdaExpressionSyntax>()
            .Any(lambda => model.GetTypeInfo(lambda, cancellation).ConvertedType is INamedTypeSymbol
            {
                Name: "Expression", ContainingNamespace: { } ns,
            } && string.Equals(ns.ToDisplayString(), "System.Linq.Expressions", StringComparison.Ordinal));

    private static GuardedReturn Returns(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
        {
            return GuardedReturn.Void;
        }

        var returned = method.ReturnType.OriginalDefinition.ToDisplayString();
        return returned switch
        {
            "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>" => GuardedReturn.Task,
            "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>" => GuardedReturn.ValueTask,
            _ => GuardedReturn.Value,
        };
    }

    // Waits for the check only where an async method may: no ref, out or in parameter, no ref struct.
    private static bool Awaits(IMethodSymbol method) =>
        Returns(method) is GuardedReturn.Task or GuardedReturn.ValueTask
        && method.Parameters.All(p => p.RefKind == RefKind.None && !p.Type.IsRefLikeType);

    private static GuardedParameter Parameter(IParameterSymbol parameter, Compilation compilation)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => parameter.IsParams ? "params " : string.Empty,
        };
        var typeOf = "typeof(" + GeneratedModelShape.DisplayName(parameter.Type, SymbolDisplayFormat.FullyQualifiedFormat, compilation)
                     + ")" + (parameter.RefKind == RefKind.None ? string.Empty : ".MakeByRefType()");
        return new GuardedParameter(
            GeneratedModelShape.DisplayName(parameter.Type, Signature, compilation), typeOf, modifier, "@" + parameter.Name);
    }
}
