using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

// What an operation's form was told about the <form> it draws — the steps a hand-written Form.Model(m) chain takes —
// kept until its render hands them on, so moving a hand form to Form.For loses none of them.
internal sealed class OperationFormLook
{
    public string? Id { get; set; }

    public string? Class { get; set; }

    public string? Style { get; set; }

    public string? ConfirmLeave { get; set; }

    public IReadOnlyDictionary<string, string?>? Data { get; set; }

    public IReadOnlyDictionary<string, string?>? Attributes { get; set; }

    public HTMLFormElement<TModel> Applied<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TModel>(
        HTMLFormElement<TModel> form) =>
        form.Id(Id).Class(Class).Style(Style).Data(Data).Attributes(Attributes).ConfirmLeave(ConfirmLeave);
}
