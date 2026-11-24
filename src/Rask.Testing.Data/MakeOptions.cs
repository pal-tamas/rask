namespace Rask.Testing;

/// <summary>
///     What a test says about the aggregate <c>Test.Make&lt;Supply&gt;(o =&gt; …)</c> makes: which factory, a value of
///     its own for a parameter, an aggregate it already made to refer to.
/// </summary>
public sealed class MakeOptions
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _given = [];

    /// <summary>
    ///     The factory to make it through, by name — <c>o.Factory = nameof(Supply.Register)</c> — for an aggregate with
    ///     more than one. Unset, the aggregate's only factory.
    /// </summary>
    public string? Factory { get; set; }

    /// <summary>What the test gave by parameter name.</summary>
    internal IReadOnlyDictionary<string, object?> Values => _values;

    /// <summary>What the test made itself, to stand in wherever its type is asked for.</summary>
    internal IReadOnlyList<object> Given => _given;

    /// <summary>
    ///     Hands <paramref name="given" /> to every parameter of its type, here and in every aggregate made on the way —
    ///     an aggregate also for a parameter that names its id: <c>o.Use(acme)</c> makes the supply and its tank both
    ///     Acme's.
    /// </summary>
    /// <param name="given">An aggregate the test stored, or any value it made.</param>
    /// <returns>The same options.</returns>
    public MakeOptions Use(object given)
    {
        ArgumentNullException.ThrowIfNull(given);
        _given.Add(given);
        return this;
    }

    /// <summary>
    ///     Hands <paramref name="value" /> to the factory's parameter named <paramref name="parameter" />:
    ///     <c>o.With("parts", parts)</c>.
    /// </summary>
    /// <param name="parameter">The parameter's name, as the factory declares it.</param>
    /// <param name="value">What it is given.</param>
    /// <returns>The same options.</returns>
    public MakeOptions With(string parameter, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        _values[parameter] = value;
        return this;
    }
}
