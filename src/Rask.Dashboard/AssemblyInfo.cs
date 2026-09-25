using Rask.Core.Routing;

// The console is its own application, mounted by AddRaskDashboard under /_rask. Its pages must never join
// the host's own route table: an app that carries this assembly without registering the dashboard — the
// battery turned off, a host assembled by hand — would otherwise route /_rask/… to pages whose
// authorization policy does not exist, and answer 500 where it should answer 404.
[assembly: MountOnlyRoutes]
