namespace Rask.Core.Forms;

// A form that asks before the reader leaves it (`ConfirmLeave`), as ReaderEdits knows the one a control is drawn in.
internal interface IGuardedForm
{
    /// <summary>The reader changed a value in a control under this form that the browser raised no event for.</summary>
    void NoteReaderEdit();
}
