using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Shared;

/// <summary>
///     How the analyzers and the quick-fix read FluentValidation rules: the in-type <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>
///     convention, and a <c>RuleFor(x =&gt; x.Member)</c> chain with the calls written on it.
/// </summary>
/// <remarks>
///     By metadata name only: the generators never reference FluentValidation, and every caller first checks that the
///     compilation does (<see cref="InlineValidatorName" /> resolves).
/// </remarks>
internal static class FluentRules
{
    /// <summary>The metadata name of <c>FluentValidation.InlineValidator&lt;T&gt;</c>.</summary>
    public const string InlineValidatorName = "FluentValidation.InlineValidator`1";

    /// <summary>The name the in-type convention gives its method.</summary>
    public const string RulesMethod = "Rules";

    private const string AbstractValidator = "AbstractValidator";
    private const string FluentValidationNamespace = "FluentValidation";

    /// <summary>Whether <paramref name="type" /> is <c>InlineValidator&lt;X&gt;</c>, handing back X.</summary>
    public static bool IsInlineValidator(ITypeSymbol type, out ITypeSymbol? validated)
    {
        validated = type is INamedTypeSymbol { Name: "InlineValidator", TypeArguments.Length: 1 } named && IsFluentType(named)
            ? named.TypeArguments[0]
            : null;
        return validated is not null;
    }

    /// <summary>
    ///     Whether <paramref name="method" /> is the in-type convention the registration finds:
    ///     <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>, T the containing type, neither of them generic.
    /// </summary>
    public static bool IsInlineRules(IMethodSymbol method) =>
        method is { Name: RulesMethod, IsStatic: true, ReturnsVoid: true, IsGenericMethod: false, Parameters.Length: 1 } &&
        IsInlineValidator(method.Parameters[0].Type, out var validated) &&
        SymbolEqualityComparer.Default.Equals(validated, method.ContainingType) &&
        !IsGenericOrInGeneric(method.ContainingType);

    /// <summary>Whether the type, or one it is nested in, is generic: generated code could not name it.</summary>
    public static bool IsGenericOrInGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The T of the <c>AbstractValidator&lt;T&gt;</c> a <c>RuleFor</c> is called on, or null when it is not one.</summary>
    public static ITypeSymbol? ValidatedBy(IInvocationOperation ruleFor)
    {
        if (!string.Equals(ruleFor.TargetMethod.Name, "RuleFor", StringComparison.Ordinal))
        {
            return null;
        }

        for (var current = ruleFor.TargetMethod.ContainingType; current is not null; current = current.BaseType)
        {
            if (current is { Name: AbstractValidator, TypeArguments.Length: 1 } && IsFluentType(current))
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    ///     The property <c>RuleFor(x =&gt; x.Member)</c> names, or null for anything but a member read straight off the
    ///     lambda's parameter.
    /// </summary>
    public static IPropertySymbol? RuledProperty(IInvocationOperation ruleFor)
    {
        if (ruleFor.Arguments.Length != 1 ||
            ruleFor.Arguments[0].Syntax is not ArgumentSyntax { Expression: SimpleLambdaExpressionSyntax lambda } ||
            lambda.Body is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member ||
            !string.Equals(receiver.Identifier.ValueText, lambda.Parameter.Identifier.ValueText, StringComparison.Ordinal) ||
            ruleFor.SemanticModel is not { } model)
        {
            return null;
        }

        return model.GetSymbolInfo(member).Symbol as IPropertySymbol;
    }

    /// <summary>
    ///     The calls written on a <c>RuleFor</c>, in order: <c>.NotEmpty().MaximumLength(200)</c> — each an extension
    ///     method taking the chain so far as its first argument, or an instance method on it.
    /// </summary>
    public static IReadOnlyList<IInvocationOperation> CallsOn(IInvocationOperation ruleFor)
    {
        var calls = new List<IInvocationOperation>();
        IOperation current = ruleFor;
        while (Next(current) is { } next)
        {
            calls.Add(next);
            current = next;
        }

        return calls;
    }

    /// <summary>The constant int arguments of a call after the chain it extends, or null when one is not a constant int.</summary>
    public static IReadOnlyList<int>? IntArguments(IInvocationOperation call)
    {
        var values = new List<int>();
        foreach (var argument in call.Arguments.Skip(call.TargetMethod.IsExtensionMethod ? 1 : 0))
        {
            if (argument.ArgumentKind == ArgumentKind.DefaultValue)
            {
                continue;
            }

            if (argument.Value.ConstantValue is { HasValue: true, Value: int value })
            {
                values.Add(value);
            }
            else if (argument.Value.Type?.SpecialType == SpecialType.System_Int32)
            {
                return null;
            }
        }

        return values;
    }

    // The call the chain so far is handed to: its parent, through the implicit conversion to the extension's receiver type.
    private static IInvocationOperation? Next(IOperation chain)
    {
        var parent = chain.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            parent = parent.Parent;
        }

        return parent switch
        {
            IArgumentOperation { Parent: IInvocationOperation call } argument when call.TargetMethod.IsExtensionMethod &&
                                                                                 call.Arguments.Length > 0 &&
                                                                                 ReferenceEquals(call.Arguments[0], argument) => call,
            IInvocationOperation call when call.Instance is { } instance && Unconverted(instance) == chain => call,
            _ => null,
        };
    }

    private static IOperation Unconverted(IOperation operation) =>
        operation is IConversionOperation { IsImplicit: true } conversion ? Unconverted(conversion.Operand) : operation;

    private static bool IsFluentType(INamedTypeSymbol type) =>
        string.Equals(type.ContainingNamespace?.ToDisplayString(), FluentValidationNamespace, StringComparison.Ordinal);
}
