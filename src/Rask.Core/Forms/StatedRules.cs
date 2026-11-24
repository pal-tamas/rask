using System.ComponentModel;
using Rask.Values;
using Rask.Wire;

namespace Rask.Core.Forms;

/// <summary>
///     Where the rules a validation library states in code (<see cref="IStatedRules" />) reach Rask: a bound control's
///     hints, a column's facets and a save's check read them through here, beside the DataAnnotations attributes.
/// </summary>
/// <remarks>
///     Filled from a <c>[ModuleInitializer]</c> of the integration package (Rask.Validation.FluentValidation); with none
///     installed every answer is "nothing stated" and costs one field read.
/// </remarks>
public static class StatedRules
{
    private static volatile IStatedRules? _source;

    /// <summary>The installed source, or null when no validation library states rules.</summary>
    internal static IStatedRules? Source => _source;

    /// <summary>
    ///     Installs the source of stated rules, replacing the one there was. Called by an integration package's
    ///     <c>[ModuleInitializer]</c>, and again (<see cref="Changed" />) when the rules it knows change.
    /// </summary>
    /// <param name="source">The rules.</param>
    public static void Use(IStatedRules source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        Changed();
    }

    /// <summary>
    ///     Forgets every answer worked out from the stated rules — a hot reload registered new ones — so the next render
    ///     asks again.
    /// </summary>
    public static void Changed() => RaskValidation.ForgetAnswers();

    /// <summary>
    ///     The shape a value object's field declares, with what its stated rules add: what its generated <c>__Field</c>
    ///     answers. Not an API to write against.
    /// </summary>
    /// <param name="type">The value object.</param>
    /// <param name="member">Its member, <c>Value</c> for a value object of one value.</param>
    /// <param name="declared">What its base record's arguments declare.</param>
    /// <returns>The shape a control bound to it is told.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static FieldShape Shape(Type type, string member, FieldShape declared)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(declared);
        return Merge(declared, FacetsOf(type, member));
    }

    /// <summary>
    ///     The column facets a value object declares, with what its stated rules add: what its generated <c>__Facets</c>
    ///     answers. The base record's arguments win where both say something. Not an API to write against.
    /// </summary>
    /// <param name="type">The value object.</param>
    /// <param name="member">Its member, <c>Value</c> for a value object of one value.</param>
    /// <param name="declared">What its base record's arguments declare.</param>
    /// <returns>The facets its column is given.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValueFacets Facets(Type type, string member, ValueFacets declared)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        var stated = FacetsOf(type, member);
        return stated.IsEmpty
            ? declared
            : new ValueFacets(
                declared.MaxLength ?? stated.MaxLength,
                declared.Precision ?? stated.Precision,
                declared.Scale ?? stated.Scale);
    }

    /// <summary>What the stated rules say about <paramref name="member" /> of <paramref name="type" />.</summary>
    /// <param name="type">The type that holds the member.</param>
    /// <param name="member">The member's name.</param>
    /// <returns>The facets; <see cref="StatedFacets.None" /> when nothing states any.</returns>
    public static StatedFacets FacetsOf(Type type, string member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return _source is { } source && source.States(type) ? source.FacetsOf(type, member) : StatedFacets.None;
    }

    /// <summary>
    ///     Asks every rule stated for <paramref name="instance" />'s type, or only those of <paramref name="members" />:
    ///     what a save asks of each row it writes.
    /// </summary>
    /// <param name="instance">The object to check.</param>
    /// <param name="members">The members to ask about, or <see langword="null" /> for every rule.</param>
    /// <param name="services">The scope a rule's own dependencies come from.</param>
    /// <param name="cancellationToken">Cancels the rules in flight.</param>
    /// <returns>One failure per broken rule, each under the member it names; none when nothing states rules for the type.</returns>
    public static ValueTask<IReadOnlyList<FieldFailure>> Check(
        object instance, IReadOnlyCollection<string>? members, IServiceProvider? services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return _source is { } source && source.States(instance.GetType())
            ? source.Check(instance, members, services, cancellationToken)
            : new ValueTask<IReadOnlyList<FieldFailure>>([]);
    }

    /// <summary>Whether anything states rules for <paramref name="type" />. Builds nothing; cheap enough for every row.</summary>
    /// <param name="type">The type to ask about.</param>
    /// <returns><see langword="true" /> when rules are stated for it.</returns>
    public static bool States(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _source?.States(type) == true;
    }

    // Whether a stated rule says the member must be filled: NotEmpty(), NotNull().
    internal static bool IsRequired(Type type, string member) => FacetsOf(type, member).Required;

    // A shape with what the stated rules add, the tightest of both where both say something: the attribute's or the value
    // object's own word for the field is never loosened.
    internal static FieldShape? Shape(Type type, string member, Type memberType, FieldShape? declared)
    {
        var stated = FacetsOf(type, member);
        if (stated.IsEmpty || stated is { MaxLength: null, MinLength: null, Min: null, Max: null, Email: false, Scale: null, Pattern: null })
        {
            return declared;
        }

        return Merge(declared ?? new FieldShape(AnnotatedShapes.KindOf(memberType)), stated);
    }

    private static FieldShape Merge(FieldShape declared, StatedFacets stated)
    {
        if (stated.IsEmpty)
        {
            return declared;
        }

        return declared with
        {
            MaxLength = Bounds.Least(declared.MaxLength, stated.MaxLength),
            MinLength = Bounds.Most(declared.MinLength, stated.MinLength),
            Min = Bounds.Most(declared.Min, stated.Min),
            Max = Bounds.Least(declared.Max, stated.Max),
            Scale = Bounds.Least(declared.Scale, stated.Scale),
            Input = declared.Input ?? (stated.Email ? InputType.Email : null),
            AutoComplete = declared.AutoComplete ?? (stated.Email ? "email" : null),
            Pattern = declared.Pattern ?? stated.Pattern,
        };
    }
}
