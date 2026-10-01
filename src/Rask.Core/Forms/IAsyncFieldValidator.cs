namespace Rask.Core.Forms;

public interface IAsyncFieldValidator
{
    ValueTask Validate(EditContext context, CancellationToken cancellationToken);
    ValueTask ValidateField(EditContext context, FieldIdentifier field, CancellationToken cancellationToken);
}
