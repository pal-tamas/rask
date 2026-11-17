using System.Reflection;

namespace Rask.Testing;

/// <summary>
///     The test running on this flow, and the lane it runs in: tests of one lane run one after another, so a test of a
///     lane that is not the one running has finished.
/// </summary>
/// <remarks>
///     Read from xUnit v3's <c>TestContext.Current</c> — its test and its collection, by their unique ids — without a
///     reference to xUnit, so the package stays independent of the test framework. Under any other framework, or outside
///     a test (a fixture setting itself up), there is none.
/// </remarks>
/// <param name="Lane">The collection the test runs in.</param>
/// <param name="Test">The test.</param>
internal readonly record struct TestScope(string Lane, string Test)
{
    private static readonly PropertyInfo? XunitCurrent = Type
        .GetType("Xunit.TestContext, xunit.v3.core", throwOnError: false)
        ?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);

    /// <summary>The test running on this flow, or <c>null</c> outside one.</summary>
    public static TestScope? Current() => Of(XunitCurrent?.GetValue(null));

    /// <summary>The scope a test context names: its <c>Test</c> and its <c>TestCollection</c>, each by its <c>UniqueID</c>.</summary>
    /// <param name="context">xUnit's <c>TestContext</c>, or <c>null</c>.</param>
    internal static TestScope? Of(object? context)
    {
        var test = UniqueId(Member(context, "Test"));
        var lane = UniqueId(Member(context, "TestCollection"));
        return test is null || lane is null ? null : new TestScope(lane, test);
    }

    private static string? UniqueId(object? item) => Member(item, "UniqueID") as string;

    private static object? Member(object? item, string name) =>
        item?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
}
