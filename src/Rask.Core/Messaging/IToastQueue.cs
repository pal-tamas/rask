namespace Rask.Core.Messaging;

// What Toast.Success(…).Heading(…).For(…) writes through: a toast is queued first and its steps change it after, which
// works because an outlet draws the queue on its next render rather than the moment a toast is added. The session's
// Toaster and Toast.Fake() both implement it.
internal interface IToastQueue
{
    int Queue(ToastLevel level, string message);

    void Change(int id, Func<ToastMessage, ToastMessage> change);
}
