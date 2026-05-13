namespace Rask.Core.Forms;

public interface IAsyncFieldValidator
{
    ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken);
    ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken cancellationToken);
}
