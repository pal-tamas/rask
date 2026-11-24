using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Data;

namespace Rask.Testing;

/// <summary>
///     A sample of one member of a value object declared from <see cref="Value" />, whose rules are its own code and
///     so cannot be read: a filled text, the first number, date or time, an enum's first member, or another value
///     object made by <c>Test.Make&lt;T&gt;()</c>.
/// </summary>
internal static class MemberSample
{
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "A test helper over the test's own value objects, which a test project never trims.")]
    public static object For(string name, ParameterInfo member)
    {
        var type = Nullable.GetUnderlyingType(member.ParameterType) ?? member.ParameterType;
        if (type == typeof(string))
        {
            return "ABCDEFGH";
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type.IsEnum)
        {
            return Enum.GetValuesAsUnderlyingType(type) is { Length: > 0 } members
                ? Enum.ToObject(type, members.GetValue(0)!)
                : Activator.CreateInstance(type)!;
        }

        if (typeof(Value).IsAssignableFrom(type))
        {
            return ValueSample.Make(type);
        }

        return SampleSteps.First(type)
            ?? throw new InvalidOperationException(
                $"Test.Make<{name}>() has no sample for its member {member.Name}, a {type.Name}. "
                + $"Make this one by hand, with {name}.Create(…).");
    }
}
