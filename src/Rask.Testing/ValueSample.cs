using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Testing;

/// <summary>
///     Builds a value object its rules accept (<see cref="Test.Make{T}" />): reads what it hands its base, samples each
///     member from that, and makes it through its generated <c>Create</c>.
/// </summary>
internal static class ValueSample
{
    private const DynamicallyAccessedMemberTypes Kept = DynamicallyAccessedMemberTypes.PublicMethods;

    public static object Make([DynamicallyAccessedMembers(Kept)] Type type)
    {
        var create = CreateOf(type);
        var members = create.GetParameters()[..^1];
        var rules = DeclaredRules.Read(() => Invoke(create, [.. members.Select(static m => Placeholder(m.ParameterType))]));
        var values = Samples(type, members, rules);

        try
        {
            return Invoke(create, values);
        }
        catch (RaskValidationException refused)
        {
            throw new InvalidOperationException(
                $"Test.Make<{type.Name}>() made {type.Name}({string.Join(", ", values.Select(Shown))}), which its rules refuse: "
                + string.Join(" ", refused.Failures.Select(static f => f.Message))
                + $" Make this one by hand, with {type.Name}.Create(…).",
                refused);
        }
    }

    // The generated `Create(members…, string? field = null)`: the one way in that asks every rule.
    private static MethodInfo CreateOf([DynamicallyAccessedMembers(Kept)] Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m =>
            string.Equals(m.Name, "Create", StringComparison.Ordinal) && m.ReturnType == type && m.GetParameters() is [.., { Name: "field" } last] && last.ParameterType == typeof(string))
        ?? throw new InvalidOperationException(
            $"Test.Make<{type.Name}>() needs the Create the Rask generator writes: declare {type.Name} as a public "
            + "`partial record` in a project the generator runs on.");

    private static object?[] Samples(Type type, ParameterInfo[] members, IValueRules? rules) =>
        (rules, members) switch
        {
            (TextRules text, [_]) => [TextSample.For(type.Name, text)],
            (NumberRules number, [var member]) => [Convert.ChangeType(NumberSample.For(number), Held(member), CultureInfo.InvariantCulture)],
            (WholeRules whole, [var member]) => [Convert.ChangeType(WholeSample.For(whole), Held(member), CultureInfo.InvariantCulture)],
            (DayRules day, [_]) => [DaySample.For(day)],
            (MomentRules moment, [_]) => [MomentSample.For(moment)],
            (RangeRules range, [var start, _]) => RangeSample.For(type.Name, Held(start), range),
            _ => [.. members.Select(member => MemberSample.For(type.Name, member))],
        };

    private static Type Held(ParameterInfo member) =>
        Nullable.GetUnderlyingType(member.ParameterType) ?? member.ParameterType;

    // What the base is handed while its rules are read: nothing is checked, so any value of the type will do.
    private static object? Placeholder(Type type)
    {
        var held = Nullable.GetUnderlyingType(type) ?? type;
        if (held == typeof(string))
        {
            return "";
        }

        return held.IsValueType ? Activator.CreateInstance(held) : null;
    }

    private static object Invoke(MethodInfo create, object?[] members)
    {
        try
        {
            return create.Invoke(null, [.. members, null])!;
        }
        catch (TargetInvocationException thrown) when (thrown.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static string Shown(object? value) =>
        value switch
        {
            null => "null",
            string text => '"' + text + '"',
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
}
