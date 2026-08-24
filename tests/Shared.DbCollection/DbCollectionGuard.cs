using System.Reflection;

namespace Rask.Tests.Shared;

/// <summary>
///     Asserts that every test class in a suite backed by one shared <c>DbContext</c> is either in the
///     xUnit collection that serialises them, or named as an exception.
/// </summary>
/// <remarks>
///     <para>
///         The race this defends is not reproducible on demand. EF Core's model cache is per process and
///         keyed on the context type — not per <c>ServiceProvider</c> — so two test classes racing to
///         first-touch one context drive a single <c>IModel</c> from two threads, and that only ever
///         surfaced on the full-solution gate (#769, #772). A guard that waits for the race to happen is
///         a guard that never fires. This one fires when the <i>shape</i> comes back, which is when
///         somebody adds a test class without knowing the rule.
///     </para>
///     <para>
///         It asks the question the safe way round. "Does this class build a context?" cannot be
///         answered from metadata — these suites build theirs in a local
///         (<c>await using var harness = new CacheHarness()</c>), which no amount of reflection over
///         fields and signatures can see, so a guard shaped that way would pass every class for the
///         wrong reason. So the default is <b>collected</b>, and a class that genuinely does not touch
///         the database is listed by name at the call site. Each name is a judgement somebody made, and
///         listing it is how the judgement gets recorded.
///     </para>
/// </remarks>
public static class DbCollectionGuard
{
    /// <summary>
    ///     Fails when a test class in <paramref name="assembly" /> is neither in
    ///     <paramref name="collectionName" /> nor named in <paramref name="exempt" />.
    /// </summary>
    /// <param name="assembly">The test assembly to scan.</param>
    /// <param name="collectionName">The collection that serialises the DB-touching classes.</param>
    /// <param name="exempt">
    ///     Classes that never build a context — pure unit tests over options, serializers and the like.
    /// </param>
    public static void AssertEveryTestClassIsCollected(
        Assembly assembly, string collectionName, params string[] exempt)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(collectionName);
        ArgumentNullException.ThrowIfNull(exempt);

        var stale = exempt.Where(name => assembly.GetTypes().All(t => t.Name != name)).ToList();
        Assert.True(
            stale.Count == 0,
            $"The exempt list names classes that no longer exist: {string.Join(", ", stale)}. "
            + "Drop them — a stale name silently exempts nothing and hides the next one.");

        var uncollected = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (!IsTestClass(type) || exempt.Contains(type.Name, StringComparer.Ordinal))
            {
                continue;
            }

            if (!string.Equals(CollectionOf(type), collectionName, StringComparison.Ordinal))
            {
                uncollected.Add(type.Name);
            }
        }

        Assert.True(
            uncollected.Count == 0,
            $"These test classes are neither in the \"{collectionName}\" collection nor exempt: "
            + string.Join(", ", uncollected.Order(StringComparer.Ordinal))
            + ". EF Core's model cache is per process and keyed on the context type, so a class that "
            + "builds one and runs in parallel with another that does drives a single IModel from two "
            + $"threads. Add [Collection(\"{collectionName}\")] — or, if the class never builds a "
            + "context, add its name to this guard's exempt list.");
    }

    // xUnit v2's CollectionAttribute takes the name as a constructor argument and exposes no property
    // for it, so the attribute DATA is the only place to read it back from.
    private static string? CollectionOf(Type type)
    {
        foreach (var data in type.GetCustomAttributesData())
        {
            if (data.AttributeType == typeof(CollectionAttribute)
                && data.ConstructorArguments is [{ Value: string name }])
            {
                return name;
            }
        }

        return null;
    }

    private static bool IsTestClass(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(static m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any());
}
