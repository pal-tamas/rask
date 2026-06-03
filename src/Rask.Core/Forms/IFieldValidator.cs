namespace Rask.Core.Forms;

public interface IFieldValidator
{
    void Validate(EditContext context);
    void ValidateField(EditContext context, FieldIdentifier field);
}
