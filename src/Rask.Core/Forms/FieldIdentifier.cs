using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

/// <summary>
///     Which field an <see cref="EditContext" /> is talking about: the object that owns it, plus the
///     property name on that object.
/// </summary>
/// <remarks>
///     The owner is the immediate one, not the form's root — so a field on a nested sub-object is
///     identified by that sub-object, and <c>Address.Street</c> on two different addresses are two
///     different fields with no string path to assemble or parse.
///     <para>
///         Identity is by REFERENCE on the model and by value on the name. Two identical-looking models
///         are therefore different fields, and replacing a model instance invalidates the identifiers
///         that referred to the old one.
///     </para>
/// </remarks>
public readonly struct FieldIdentifier : IEquatable<FieldIdentifier>
{
    /// <summary>The object that owns the field — the immediate owner, not the form's root model.</summary>
    public object Model { get; }

    /// <summary>The property name on <see cref="Model" />.</summary>
    public string FieldName { get; }

    /// <summary>Identifies <paramref name="fieldName" /> on <paramref name="model" />.</summary>
    /// <param name="model">The object that owns the field.</param>
    /// <param name="fieldName">The property name on that object.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public FieldIdentifier(object model, string fieldName)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    /// <summary>
    ///     Whether both identify the same field: the same model INSTANCE, by reference, and the same name.
    /// </summary>
    /// <param name="other">The identifier to compare with.</param>
    public bool Equals(FieldIdentifier other) =>
        ReferenceEquals(Model, other.Model) && FieldName == other.FieldName;

    /// <inheritdoc cref="Equals(FieldIdentifier)" />
    public override bool Equals(object? obj) => obj is FieldIdentifier f && Equals(f);

    /// <summary>A hash consistent with <see cref="Equals(FieldIdentifier)" />, so this works as a
    ///     dictionary key.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(Model), FieldName);

    /// <summary><c>TypeName.FieldName</c>, for diagnostics.</summary>
    public override string ToString() => $"{Model.GetType().Name}.{FieldName}";
}
