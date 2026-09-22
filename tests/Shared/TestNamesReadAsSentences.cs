using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Rask.Tests.Conventions;

// Linked into every test project by tests/Directory.Build.props, so each project checks its own tests and the
// check runs exactly when that project does — the pre-commit gate runs only the projects a change touches.
//
// A test's name is the sentence the runner prints and the first thing a reader of a failure sees, so it says
// what the test proves in plain English: `Remember_loads_once_then_serves_from_the_cache`, not
// `GetOrCreate_RunsFactoryOnce`. Names the test is about keep their casing — `Cache`, `OnMount`, `RASK092` —
// and stay the minority.
public sealed partial class TestNamesReadAsSentences
{
    private static readonly HashSet<string> Scaffolding =
        new(StringComparer.Ordinal) { "Should", "Given", "When", "Then", "Test", "Returns", "Throws" };

    [Fact]
    public void Every_test_in_this_project_is_named_as_a_sentence()
    {
        var offenders = typeof(TestNamesReadAsSentences).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
            .Select(method => (Name: $"{method.DeclaringType!.Name}.{method.Name}", Why: WhyNotASentence(method.Name)))
            .Where(test => test.Why is not null)
            .OrderBy(test => test.Name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Test names read as sentences: {offenders.Count} do not\n"
            + string.Join('\n', offenders.Select(test => $"  {test.Name} — {test.Why}"))
            + "\n    write each as what it proves, e.g. Remember_loads_once_then_serves_from_the_cache");
    }

    private static string? WhyNotASentence(string name)
    {
        var words = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
        {
            return "fewer than three words";
        }

        if (words.Count(word => Prose().IsMatch(word)) * 2 <= words.Length)
        {
            return "mostly names, not prose";
        }

        return words.FirstOrDefault(Scaffolding.Contains) is { } scaffold ? $"scaffolding word \"{scaffold}\"" : null;
    }

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex Prose();
}
