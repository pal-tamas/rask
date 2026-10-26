using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Messaging;

namespace Rask.Core;

public partial class Component
{
    // A published event, handed to this component's Handle in its session's event (LiveListeners): the event's
    // services ambient, cancelled with the component, and a fault answered as a handler's is — the handler-fault toast.
    internal async Task HandleLiveEvent(Func<object, object, Task> handle, object e, IServiceProvider services)
    {
        if (IsTornDown)
        {
            return;
        }

        using var dispatchScope = DispatchServicesScope.Push(services);
        using var owner = EventServices.Dispatching(services, this);
        using var cultureScope = Globalization.RaskCultureScope.PushFrom(services);
        using var ambientServices = Ambient.Enter(services);
        using var ambientToken = Ambient.Enter(LifetimeToken);

        Live.StateDirty = true;
        try
        {
            await handle(this, e).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCancellationOfALeftComponent(this, ex))
        {
            ReportCancelledAfterLeaving(this, ex);
        }
        catch (Exception ex) when (Refusal.Find(ex) is { } refusal && Refusal.Say(refusal))
        {
            ReportRefused(this, refusal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ResolveHandlerBoundary(this) is { } boundary)
        {
            AnswerHandlerFault(boundary, this, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live", $"Rask Live: {GetType().Name}.Handle({e.GetType().Name}) threw", ex);
        }
    }
}
