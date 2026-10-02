using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core.Live;

namespace Rask.Web.Tests;

// A host's own runtime, as far as an event's live field needs one: its JSON options read {"__jsObjectId": 5} as a handle
// to an object the browser holds, which FakeBrowser's cannot. Each call it makes is recorded and answers Answer (1) at once.
internal sealed class HostedBrowser : RaskJSRuntimeBase
{
    public List<PendingJsInvoke> Calls { get; } = [];

    // The JSON each call answers with.
    public string Answer { get; set; } = "1";

    protected override ILiveJsHost CurrentHost => throw new NotSupportedException("A test's runtime has no session.");

    protected override void DispatchOutsideRender(PendingJsInvoke invoke)
    {
        Calls.Add(invoke);
        DotNetDispatcher.EndInvokeJS(this, $"[{invoke.TaskId},true,{Answer}]");
    }

    protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
    {
    }
}
