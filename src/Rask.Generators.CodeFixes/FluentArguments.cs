using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.CodeFixes;

// The base-record arguments a FluentValidation check says: MaximumLength(200) is MaxLength: 200, InclusiveBetween(0, 100)
// is Min: 0, Max: 100, PrecisionScale(9, 2, false) is Precision: 9, Scale: 2. True when the check is taken over (and so
// taken off the rule); false for one no argument says — Matches, EmailAddress, Must — which stays where it is.
internal static class FluentArguments
{
    public static bool Read(IInvocationOperation call, ValueBase valueBase, bool nullable, BaseArguments arguments)
    {
        if (Constants(call) is not { } given)
        {
            return false;
        }

        switch (call.TargetMethod.Name)
        {
            // A filled text is what Text asks by default; a non-nullable member is always given.
            case "NotEmpty" when valueBase.IsText && !nullable && given.Count == 0:
                arguments.Required = true;
                return true;
            case "NotNull" when !nullable && given.Count == 0:
                return true;
        }

        if (valueBase.IsText)
        {
            return call.TargetMethod.Name switch
            {
                "MaximumLength" when given.Count == 1 => Set(arguments, false, ("MaxLength", given[0])),
                "MinimumLength" when given.Count == 1 => Set(arguments, false, ("MinLength", given[0])),
                "Length" when given.Count == 1 => Set(arguments, false, ("MinLength", given[0]), ("MaxLength", given[0])),
                "Length" when given.Count == 2 => Set(arguments, false, ("MinLength", given[0]), ("MaxLength", given[1])),
                _ => false,
            };
        }

        var whole = valueBase.IsInteger;
        return call.TargetMethod.Name switch
        {
            "InclusiveBetween" when valueBase.IsNumeric && given.Count == 2 => Set(arguments, whole, ("Min", given[0]), ("Max", given[1])),
            "GreaterThanOrEqualTo" when valueBase.IsNumeric && given.Count == 1 => Set(arguments, whole, ("Min", given[0])),
            "LessThanOrEqualTo" when valueBase.IsNumeric && given.Count == 1 => Set(arguments, whole, ("Max", given[0])),
            "PrecisionScale" when valueBase.IsNumeric && !whole && given.Count >= 2 =>
                Set(arguments, true, ("Precision", given[0]), ("Scale", given[1])),
            _ => false,
        };
    }

    // The constants the check is given after the chain it extends; null when one is not a constant.
    private static List<object?>? Constants(IInvocationOperation call)
    {
        var given = new List<object?>();
        foreach (var argument in call.Arguments.Skip(call.TargetMethod.IsExtensionMethod ? 1 : 0))
        {
            if (argument.ArgumentKind == ArgumentKind.DefaultValue)
            {
                continue;
            }

            if (!argument.Value.ConstantValue.HasValue)
            {
                return null;
            }

            given.Add(argument.Value.ConstantValue.Value);
        }

        return given;
    }

    // Every value written as its parameter takes it, or none at all.
    private static bool Set(BaseArguments arguments, bool whole, params (string Parameter, object? Value)[] values)
    {
        var literals = values.Select(v => (v.Parameter, Literal: BaseArguments.Literal(v.Value, whole))).ToList();
        if (literals.Exists(static l => l.Literal is null))
        {
            return false;
        }

        foreach (var (parameter, literal) in literals)
        {
            if (!(string.Equals(literal, "0", StringComparison.Ordinal) && parameter.EndsWith("Length", StringComparison.Ordinal)))
            {
                arguments.Set(parameter, literal!);
            }
        }

        return true;
    }
}
