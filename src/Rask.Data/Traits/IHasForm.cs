namespace Rask.Data;

/// <summary>
/// The aggregate gets a generated form model, with <c>X.Create(model)</c> and <c>X.Update(id, model)</c>:
/// <see cref="IHasCreateForm" /> and <see cref="IHasUpdateForm" /> together.
/// </summary>
public interface IHasForm : IHasCreateForm, IHasUpdateForm;
