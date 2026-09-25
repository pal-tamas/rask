namespace Rask.Core.Forms;

// Non-generic marker every IFormControl<T> carries, so the render machinery can recognise a form
// control without knowing its value type T (Component.GetOrCreateChild records the control's creating
// parent through it — see BindingConsumerRegistry). No members: it is purely a type tag.
public interface IFormControl;
