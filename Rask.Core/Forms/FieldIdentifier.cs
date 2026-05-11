using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

public readonly struct FieldIdentifier : IEquatable<FieldIdentifier>
{
    public object Model { get; }
    public string FieldName { get; }

    public FieldIdentifier(object model, string fieldName)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    public bool Equals(FieldIdentifier other) =>
        ReferenceEquals(Model, other.Model) && FieldName == other.FieldName;

    public override bool Equals(object? obj) => obj is FieldIdentifier f && Equals(f);

    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(Model), FieldName);

    public override string ToString() => $"{Model.GetType().Name}.{FieldName}";
}
