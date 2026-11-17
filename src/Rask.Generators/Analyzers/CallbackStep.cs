using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Which Rask event or binding callback a lambda is handed to — <c>.OnClick(() =&gt; …)</c>,
///     <c>.AfterBind(_ =&gt; …)</c> — for the analyzers that reason about what runs later, when the event fires.
/// </summary>
internal static class CallbackStep
{
    /// <summary>
    ///     The callback's name when <paramref name="lambda" /> is the argument of a Rask callback step (a generated
    ///     chain step, or a generated factory's parameter, named <c>On…</c> or <c>AfterBind</c>); otherwise null.
    /// </summary>
    /// <remarks>
    ///     The lambda's parent is its delegate creation; that delegate's parent is the argument, whose parent is
    ///     the invocation. Both halves are required — a generated step or factory, AND a callback by name — so a
    ///     user helper that merely takes an <c>Action</c> is left alone, as is a template or a selector.
    /// </remarks>
    public static string? NameOf(IOperation lambda)
    {
        IArgumentOperation? argument = null;
        for (var op = lambda.Parent; op is not null && argument is null; op = op.Parent)
        {
            switch (op)
            {
                case IArgumentOperation arg:
                    argument = arg;
                    break;
                case IConversionOperation or IDelegateCreationOperation or IParenthesizedOperation:
                    continue; // transparent wrappers between the lambda and its argument
                default:
                    return null;
            }
        }

        if (argument?.Parameter is not { } parameter
            || argument.Parent is not IInvocationOperation call)
        {
            return null;
        }

        // On the chain the callback's NAME is the step's, not the parameter's (every step takes a parameter
        // called `value`) — `.AfterBind(_ => …)`.
        if (BuilderEntry.IsChainStep(call.TargetMethod))
        {
            return IsCallbackName(call.TargetMethod.Name) ? call.TargetMethod.Name : null;
        }

        return IsGeneratedFactory(call.TargetMethod) && IsCallbackName(parameter.Name)
            ? parameter.Name
            : null;
    }

    private static bool IsGeneratedFactory(IMethodSymbol method) =>
        method.IsStatic && string.Equals(method.ContainingType?.Name, "Generated", StringComparison.Ordinal);

    // By NAME, because the delegate type the step takes no longer carries the signal: a step's overloads take
    // plain `Action`/`Func<…>` and build the `Callback` themselves, and a BCL delegate says nothing about who
    // runs it later. What still says it is the property: `On…` is Rask's event-callback convention across the
    // whole surface, and `AfterBind` is the binding hook that does not use the prefix.
    //
    // A `Func<T, Component>` template or a `Func<T, string?>` selector is not an event and is never matched.
    private static bool IsCallbackName(string name) =>
        name is "AfterBind" or "AfterBindAsync"
        || (name.StartsWith("On", StringComparison.Ordinal) && name.Length > 2 && char.IsUpper(name[2]));
}
