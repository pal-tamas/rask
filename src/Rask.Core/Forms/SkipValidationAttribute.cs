namespace Rask.Core.Forms;

/// <summary>
///     Leaves a property, or every object of a type, out of the walk validation makes of a form's model: nothing
///     under it is validated, and nothing under it is read.
/// </summary>
/// <remarks>
///     <para>
///         Validation walks the model's object graph on every check, so that a rule on an address inside an order
///         is heard without saying so. On a property that leads somewhere validation must not go — an entity's
///         navigation to the rest of the database, a cache, a service — put <c>[SkipValidation]</c>; on a type that is
///         never the form's to validate, put it on the class. The model itself is always validated: the mark keeps
///         the walk from going <em>into</em> what it is on.
///     </para>
///     <para>
///         ASP.NET Core's own <c>[SkipValidation]</c> (<c>Microsoft.Extensions.Validation</c>) is honoured the same
///         way, so a model shared with a minimal API needs one mark.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property, Inherited = true)]
public sealed class SkipValidationAttribute : Attribute;
