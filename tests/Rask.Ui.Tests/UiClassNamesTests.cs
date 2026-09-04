using System.Reflection;

namespace Rask.Ui.Tests;

/// <summary>
///     Every class name the kit writes exists in the stylesheet the kit ships.
/// </summary>
/// <remarks>
///     <para>
///         This is the guard the whole kit rests on. daisyUI 5 emits a component's CSS only where Tailwind
///         can SEE its class name in the scanned source, so a name that is built at runtime rather than
///         written as a literal is absent from the compiled sheet — and the component then renders with no
///         styling whatsoever while the build stays green and the markup carries exactly the class the call
///         site asked for. There is no error, no warning and no visual clue short of opening the page.
///     </para>
///     <para>
///         Reflecting over the tables rather than listing them here is deliberate: a new component adds its
///         own switch and is covered the moment it exists, instead of when somebody remembers to extend a
///         list in a test.
///     </para>
/// </remarks>
public sealed class UiClassNamesTests
{
    [Fact]
    public void Every_name_the_kit_can_write_is_defined_in_the_shipped_sheet()
    {
        var missing = new List<string>();

        foreach (var (table, name) in AllNames())
        {
            // As a class SELECTOR. Merely appearing as a substring would also match, say, `btn-primary`
            // living inside some other rule's selector list, which is not the same as being defined.
            if (!UiStylesheet.Css.Contains("." + name, StringComparison.Ordinal))
            {
                missing.Add($"{table} -> .{name}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void The_tables_are_not_empty()
    {
        // Guards the reflection above: a rename that stopped it finding any table would make the test
        // pass by having nothing to check.
        Assert.NotEmpty(AllNames());
    }

    [Fact]
    public void No_name_is_built_by_concatenation()
    {
        // Every value a table returns must be a compile-time constant of the switch, which is what makes
        // it visible to Tailwind's scan. A value carrying a space is a composed class list, and a value
        // ending in a hyphen is the signature of a half-built name.
        foreach (var (table, name) in AllNames())
        {
            Assert.False(name.EndsWith('-'), $"{table} returned a dangling prefix: '{name}'.");
            Assert.DoesNotContain(' ', name);
        }
    }

    private static List<(string Table, string Name)> AllNames()
    {
        var type = typeof(UiStylesheet).Assembly.GetType("Rask.Ui.UiClassNames");
        Assert.NotNull(type);

        var found = new List<(string, string)>();
        foreach (var method in type!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
            {
                continue;
            }

            foreach (var value in Enum.GetValues(parameters[0].ParameterType))
            {
                if (method.Invoke(null, [value]) is string s && s.Length > 0)
                {
                    found.Add((method.Name, s));
                }
            }
        }

        return found;
    }
}
