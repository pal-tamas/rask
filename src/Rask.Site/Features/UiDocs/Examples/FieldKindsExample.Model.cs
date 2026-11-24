using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs.Examples;

// What Rask.Data's generated model registers for an aggregate's fields, written out for this data-less site: each
// field asks its value object's rules and is told its shape, both generated beside the value object.
public sealed partial class FieldKindsExample
{
    static FieldKindsExample()
    {
        RaskValidation.RegisterFieldRules(typeof(Signup), static name => name switch
        {
            nameof(Signup.Email) => (Validate<string?>)ContactEmail.__Problems,
            nameof(Signup.Phone) => (Validate<string?>)ContactPhone.__Problems,
            nameof(Signup.Site) => (Validate<string?>)Homepage.__Problems,
            nameof(Signup.Password) => (Validate<string?>)ChosenPassword.__Problems,
            nameof(Signup.Remark) => (Validate<string?>)Remark.__Problems,
            _ => null,
        });
        RaskValidation.RegisterFieldShapes(typeof(Signup), static name => name switch
        {
            nameof(Signup.Email) => ContactEmail.__Field,
            nameof(Signup.Phone) => ContactPhone.__Field,
            nameof(Signup.Site) => Homepage.__Field,
            nameof(Signup.Password) => ChosenPassword.__Field,
            nameof(Signup.Remark) => Remark.__Field,
            _ => null,
        });
    }
}
