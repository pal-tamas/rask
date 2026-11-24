using Rask.Wire;

namespace Rask.Core.Forms;

/// <summary>
///     The rules a validation library states in code for the members of a type — FluentValidation's <c>RuleFor</c> —
///     as Rask's controls, its columns and its save ask them: exactly where a DataAnnotations attribute on the member
///     would be asked. The seam an integration package fills (<see cref="StatedRules.Use" />), so Rask.Core and
///     Rask.Data reference no validation library.
/// </summary>
public interface IStatedRules
{
    /// <summary>Whether <paramref name="type" /> states any rule. Asked for every row a save writes; it builds nothing.</summary>
    /// <param name="type">An aggregate, an entity, a value object or a form model.</param>
    /// <returns><see langword="true" /> when something states rules for the type.</returns>
    bool States(Type type);

    /// <summary>What the rules stated for <paramref name="member" /> of <paramref name="type" /> say about its field.</summary>
    /// <param name="type">The type that holds the member: an aggregate, an entity, a value object or a form model.</param>
    /// <param name="member">The member's name.</param>
    /// <returns>The facets; <see cref="StatedFacets.None" /> when nothing is stated for it.</returns>
    StatedFacets FacetsOf(Type type, string member);

    /// <summary>Asks every rule stated for <paramref name="instance" />'s type, or only those of <paramref name="members" />.</summary>
    /// <param name="instance">The object to check.</param>
    /// <param name="members">The members to ask about, or <see langword="null" /> for every rule.</param>
    /// <param name="services">The scope a rule's own dependencies come from.</param>
    /// <param name="cancellationToken">Cancels the rules in flight.</param>
    /// <returns>One failure per broken rule, each under the member it names, relative to <paramref name="instance" />.</returns>
    ValueTask<IReadOnlyList<FieldFailure>> Check(
        object instance, IReadOnlyCollection<string>? members, IServiceProvider? services, CancellationToken cancellationToken);
}
