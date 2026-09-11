using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs.Client;

namespace Company.RaskServer.Browser;

/// <summary>Services the browser half needs. The server registers its own in Program.cs.</summary>
public static class BrowserStartup
{
    public static void Configure(IServiceCollection services)
    {
        // Every message this page dispatches travels to the server, over the same IDispatcher
        // call the server half makes in-process. Nothing on a message marks it remote: you
        // write a record and a handler, and where the handler lives decides where it runs.
        //
        // A client is a PURE client. A handler compiled into the bundle is BYPASSED — the
        // request goes to the server, which answers 404 for a name it has no handler for.
        // [LocalOnly] is the only way to keep a message in the browser, and it is what a
        // local counter or an offline queue needs.
        //
        // Handlers live under Server/, which the bundle does not compile — so a connection
        // string or a pricing rule cannot reach a download anybody can read.
        services.AddRaskCqrsClient();
    }
}