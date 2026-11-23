using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Rask.Data;

/// <summary>
///     One property of an aggregate or an entity and the validation attributes it carries, as the save asks them:
///     what <see cref="GeneratedDomainRules" /> is handed for each.
/// </summary>
/// <remarks>Built by generated code. Not an API to write against: put the attribute on the property.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CheckedProperty
{
    /// <summary>Describes one checked property.</summary>
    /// <param name="name">The property, as the entity and its form model both name it.</param>
    /// <param name="displayName">
    ///     What a failure calls it: its <c>[Display]</c> name, asked each time so a name from resources follows the
    ///     reader's language.
    /// </param>
    /// <param name="valueOf">
    ///     What the attributes are asked about, from what the property holds — a value object of one value hands over
    ///     that value, as its form model carries it — or <see langword="null" /> for the property's value itself.
    /// </param>
    /// <param name="checks">The attributes, in declaration order.</param>
    public CheckedProperty(
        string name, Func<string> displayName, Func<object?, object?>? valueOf, params ValidationAttribute[] checks)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(checks);

        Name = name;
        DisplayName = displayName;
        ValueOf = valueOf;
        Checks = checks;
    }

    /// <summary>The property's name.</summary>
    public string Name { get; }

    /// <summary>What a failure calls the property.</summary>
    public Func<string> DisplayName { get; }

    /// <summary>What the attributes are asked about, from the property's value; null for the value itself.</summary>
    public Func<object?, object?>? ValueOf { get; }

    /// <summary>The attributes.</summary>
    public IReadOnlyList<ValidationAttribute> Checks { get; }
}
