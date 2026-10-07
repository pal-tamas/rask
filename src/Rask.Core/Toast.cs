using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Messaging;

namespace Rask.Core;

/// <summary>
///     A short message to the person using the app, with nothing injected and nothing mounted — from an event
///     handler, a save, a render:
/// </summary>
/// <remarks>
///     <code>
///     Toast.Success("Saved");
///     Toast.Success("Your order was placed").Heading("Order 42");
///     Toast.Info("Order placed").Action("View order", () => Routes.OrderPage(order.Id).Go());
///     Toast.Error("Payment failed").For(30.Seconds);
///     Toast.Error("Couldn't reach the server").UntilDismissed();
///     </code>
///     <para>
///         A toast belongs to the session, not the page, so one raised just before navigating shows on the page the
///         person lands on. The host draws them — in the UI kit's look, or Rask's own with the kit off — until the
///         app mounts a <c>ToastOutlet</c> of its own. Where they appear and how long they stay by default is
///         <c>c.Toasts</c>.
///     </para>
/// </remarks>
public static class Toast
{
    /// <summary>Something went right: <c>Toast.Success("Saved")</c>.</summary>
    /// <param name="message">What to say.</param>
    public static ShowingToast Success(string message) => Show(ToastLevel.Success, message);

    /// <summary>Something worth knowing.</summary>
    /// <param name="message">What to say.</param>
    public static ShowingToast Info(string message) => Show(ToastLevel.Info, message);

    /// <summary>Something to watch out for.</summary>
    /// <param name="message">What to say.</param>
    public static ShowingToast Warning(string message) => Show(ToastLevel.Warning, message);

    /// <summary>Something went wrong: <c>Toast.Error("Couldn't reach the server")</c>.</summary>
    /// <param name="message">What to say.</param>
    public static ShowingToast Error(string message) => Show(ToastLevel.Error, message);

    /// <summary>
    ///     Records this test's toasts instead of showing them: <c>using var toasts = Toast.Fake();</c>, then
    ///     <c>toasts.Shown("Saved").Once()</c>.
    /// </summary>
    public static ToastFake Fake() => new();

    /// <summary>What <see cref="Fake" /> put in the way of the session's toaster, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<IToastQueue?> Faked = new();

    private static ShowingToast Show(ToastLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var queue = Resolve();
        return new ShowingToast(queue, queue.Queue(level, message));
    }

    private static IToastQueue Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var toaster = (Ambient.Services ?? throw new InvalidOperationException(
                "Toast was called outside any work in progress — an event handler, a render or a request — so there "
                + "is no session to show it in."))
            .GetRequiredService<IToaster>();

        return toaster as IToastQueue ?? new AddOnly(toaster);
    }

    // A toaster of the app's own that is not Rask's: it takes the message, and the steps after it have nowhere to go.
    private sealed class AddOnly(IToaster toaster) : IToastQueue
    {
        public int Queue(ToastLevel level, string message)
        {
            toaster.Add(level, message);
            return -1;
        }

        public void Change(int id, Func<ToastMessage, ToastMessage> change)
        {
        }
    }
}
