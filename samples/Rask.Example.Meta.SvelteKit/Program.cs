using Rask.Cqrs;
using Rask.Cqrs.Server;
using Rask.Meta.Hosting;
var builder = WebApplication.CreateBuilder(args);

// AddRaskCqrsServer registers the mediator AND the endpoint pair the front end dispatches
// through. The TypeScript the front end imports is generated from these same message records
// at build time, so the two halves cannot disagree about a payload or a result.
//
// RequireAuthenticatedUser is OFF because this template has no authentication to require —
// left on, every message would answer 401 and nothing would work. Add a cookie or JWT scheme
// and DELETE this argument: the default is on for a reason.
builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);

builder.Services.AddSingleton<Rask.Example.Meta.SvelteKit.Features.Hello.VisitCounter>();

// A liveness/readiness endpoint (mapped below). `rask deploy` probes it to gate the
// blue-green swap; also useful for any load balancer or orchestrator.
builder.Services.AddHealthChecks();

// The front end's Node server, supervised as a child process on loopback. The framework is
// the one the .csproj named — read from the assembly, so the build and the running host
// cannot disagree about which one was built.
//
// Set o.BaseUrl to this host's own loopback address if you dispatch from a SERVER render: a
// relative URL has no origin in Node, and the value is deliberately configured rather than
// derived from the incoming request.
builder.Services.AddRaskMeta(o =>
{
    // Where a SERVER render dispatches to. A route module in client/ runs in Node before it ever runs
    // in a browser, and a relative URL has no origin there — so the host hands the child process its
    // own address in RASK_BASE_URL. Taken from the URL this host was told to listen on.
    o.BaseUrl = builder.Configuration["urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
});
var app = builder.Build();
// The endpoint pair every dispatched message arrives on: GET for queries, POST for commands,
// both under /_rask/cqrs/request/{name}. Two routes however many messages the app grows, and
// the verb carries what IQuery and ICommand already declare — so a command is 405 on GET and
// cannot be triggered by a URL, a prefetch or a link scanner.
//
// Mapped BEFORE UseRaskMeta. That call ends the pipeline with a fallback that forwards to the
// front end, so an endpoint added after it would be answered with a rendered page instead —
// which is the one failure of this lane that looks like a front-end bug.
app.MapRaskCqrs();

app.MapHealthChecks("/healthz");

// Serves the framework's built client assets from Kestrel (one hop less per asset, and the
// immutable cache headers written for you) and forwards everything else to the node process.
// Before the port answers, requests get 503 with Retry-After rather than a 502 from
// forwarding into a closed socket.
app.UseRaskMeta();

app.Run();

