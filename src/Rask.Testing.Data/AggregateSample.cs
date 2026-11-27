using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Rask.Cqrs;
using Rask.Data;
using Rask.Values;

namespace Rask.Testing;

/// <summary>
///     One <c>Test.Make&lt;T&gt;()</c>: the aggregate made through its own factory, each parameter given a value its rules
///     take, every aggregate it refers to made the same way and stored first — once each, so a supply and its tank
///     share a company — and then the aggregate itself saved, its rules asked.
/// </summary>
internal sealed class AggregateSample(MakeOptions options, Type root)
{
    // What a reference's factory is handed of the test's own: nothing, the options' values being the root's.
    private static readonly IReadOnlyDictionary<string, object?> NoValues = new Dictionary<string, object?>(StringComparer.Ordinal);

    private readonly Dictionary<Type, object> _made = [];
    private readonly HashSet<Type> _making = [];
    private readonly NullabilityInfoContext _nullability = new();

    /// <summary>Makes and stores <paramref name="aggregate" />; the options' values go to its factory.</summary>
    public async Task<object> Make(Type aggregate)
    {
        foreach (var given in options.Given.Where(static g => g is IAggregate))
        {
            _made.TryAdd(given.GetType(), given);
        }

        return await Stored(aggregate, options.Values, options.Factory).ConfigureAwait(false);
    }

    private async Task<object> Stored(Type aggregate, IReadOnlyDictionary<string, object?> values, string? named)
    {
        if (!_making.Add(aggregate))
        {
            throw new InvalidOperationException(
                $"Test.Make<{root.Name}>() went round in a circle: {aggregate.Name} refers, through what it refers to, to "
                + $"itself. Make one of them first and hand it over with o.Use(…).");
        }

        var factory = AggregateFactory.Of(aggregate, named);
        var parameters = factory.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            arguments[i] = parameter.Name is { } name && values.TryGetValue(name, out var value)
                ? value
                : await Sample(aggregate, parameter).ConfigureAwait(false);
        }

        try
        {
            var made = await AggregateFactory.Awaited(Invoke(factory, arguments)).ConfigureAwait(false);
            await Save(aggregate, made).ConfigureAwait(false);
            _making.Remove(aggregate);
            return made;
        }
        catch (RaskValidationException refused)
        {
            throw new InvalidOperationException(
                $"Test.Make<{root.Name}>() made a {aggregate.Name} through {factory.Name}(…) that its rules refuse: "
                + string.Join(" ", refused.Failures.Select(static f => f.Fields is [_, ..] at ? $"{string.Join(", ", at)}: {f.Message}" : f.Message))
                + $" Hand it the values its rules take: Test.Make<{root.Name}>(o => o.With(\"…\", …)).",
                refused);
        }
    }

    private Task<object?> Sample(Type aggregate, ParameterInfo parameter) =>
        parameter.HasDefaultValue && options.Given.FirstOrDefault(parameter.ParameterType.IsInstanceOfType) is null
            ? Task.FromResult(parameter.DefaultValue)
            : Sample(aggregate, parameter.Name ?? "", parameter.ParameterType, IsNullable(parameter));

    private async Task<object?> Sample(Type aggregate, string name, Type type, bool nullable)
    {
        if (options.Given.FirstOrDefault(type.IsInstanceOfType) is { } given)
        {
            return given;
        }

        if (typeof(IAggregate).IsAssignableFrom(type))
        {
            return await Reference(type).ConfigureAwait(false);
        }

        if (nullable || type == typeof(CancellationToken))
        {
            return null;
        }

        if (AggregateKeys.Owner(type, name, [root.Assembly, aggregate.Assembly]) is { } owner)
        {
            return AggregateKeys.IdOf(await Reference(owner).ConfigureAwait(false));
        }

        if (MemberSample.Of(type) is { } plain)
        {
            return plain;
        }

        if (type == typeof(Guid))
        {
            return Uuid.Create();
        }

        if (Rows(type) is { } row)
        {
            return await OneRow(aggregate, name, row, type).ConfigureAwait(false);
        }

        if (Spec(type) is { } constructor)
        {
            return await Built(aggregate, constructor).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Test.Make<{root.Name}>() has no sample for {aggregate.Name}'s {name}, a {type.Name}. "
            + $"Hand it one: Test.Make<{root.Name}>(o => o.With(\"{name}\", …)), or o.Use(…) for every {type.Name}.");
    }

    private async Task<object> Reference(Type aggregate)
    {
        if (_made.TryGetValue(aggregate, out var made))
        {
            return made;
        }

        made = await Stored(aggregate, NoValues, null).ConfigureAwait(false);
        _made[aggregate] = made;
        return made;
    }

    // A list of rows: one row, sampled as a parameter is.
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "A test helper over the test's own row specs, which a test project never compiles ahead of time.")]
    private async Task<object> OneRow(Type aggregate, string name, Type row, Type rows)
    {
        var sample = await Sample(aggregate, name, row, nullable: false).ConfigureAwait(false);
        if (rows.IsArray)
        {
            var array = Array.CreateInstance(row, 1);
            array.SetValue(sample, 0);
            return array;
        }

        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(row))!;
        list.Add(sample);
        return list;
    }

    // A row spec or any record: through its public constructor, each member sampled.
    private async Task<object> Built(Type aggregate, ConstructorInfo constructor)
    {
        var members = constructor.GetParameters();
        var values = new object?[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            values[i] = await Sample(aggregate, members[i]).ConfigureAwait(false);
        }

        return Invoke(constructor, values)!;
    }

    // The element of a list a factory takes: an array, a List<T>, or an interface a List<T> is.
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "A test helper over the test's own row specs, which a test project never compiles ahead of time.")]
    private static Type? Rows(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type is not { IsGenericType: true } || type == typeof(string))
        {
            return null;
        }

        var element = type.GetGenericArguments() is [var only] ? only : null;
        return element is not null && type.IsAssignableFrom(typeof(List<>).MakeGenericType(element)) ? element : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "A test helper over the test's own row specs, which a test project never trims.")]
    private static ConstructorInfo? Spec(Type type) =>
        type is { IsClass: true, IsAbstract: false } && !typeof(Value).IsAssignableFrom(type)
            ? type.GetConstructors().OrderByDescending(static c => c.GetParameters().Length).FirstOrDefault()
            : null;

    private bool IsNullable(ParameterInfo parameter) =>
        Nullable.GetUnderlyingType(parameter.ParameterType) is not null
        || (!parameter.ParameterType.IsValueType && _nullability.Create(parameter).WriteState == NullabilityState.Nullable);

    // The save the aggregate's own `Save()` is: its rules asked, on the database the test started.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "A test helper over the test's own aggregates, which a test project never trims.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "A test helper over the test's own aggregates, which a test project never compiles ahead of time.")]
    private async Task Save(Type aggregate, object made)
    {
        var save = typeof(GeneratedModelWrites).GetMethod(nameof(GeneratedModelWrites.Save))!.MakeGenericMethod(aggregate);
        await AggregateFactory.Awaited(Invoke(save, [made, null, CancellationToken.None])).ConfigureAwait(false);
    }

    private static object? Invoke(MethodBase method, object?[] arguments)
    {
        try
        {
            return method is ConstructorInfo constructor ? constructor.Invoke(arguments) : method.Invoke(null, arguments);
        }
        catch (TargetInvocationException thrown) when (thrown.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
