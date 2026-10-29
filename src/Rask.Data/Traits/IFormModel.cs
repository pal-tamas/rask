namespace Rask.Data;

/// <summary>The aggregate gets a generated form model, with <c>X.Create(model)</c> and <c>X.Update(id, model)</c>.</summary>
/// <remarks>
/// Not <c>IModel</c>: that is EF Core's <c>Microsoft.EntityFrameworkCore.Metadata.IModel</c>, and a file using both
/// namespaces could not name either.
/// </remarks>
#pragma warning disable CA1040 // a composite: what it extends IS the declaration, read by the generator
public interface IFormModel : ICreateFormModel, IUpdateFormModel;
#pragma warning restore CA1040
