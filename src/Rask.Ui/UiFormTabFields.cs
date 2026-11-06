using Rask.Core.Forms;

namespace Rask;

/// <summary>What one <see cref="UiFormTab" /> holds: fields by name, and every field of the objects it reaches.</summary>
internal sealed class UiFormTabFields(HashSet<FieldIdentifier> fields, HashSet<object> owners)
{
    /// <summary>Whether <paramref name="field" /> is one this tab holds.</summary>
    internal bool Holds(FieldIdentifier field) => fields.Contains(field) || owners.Contains(field.Model);
}
