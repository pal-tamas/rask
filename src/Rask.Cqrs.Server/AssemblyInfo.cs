using Rask.Cqrs;

// Marks this assembly as a remote transport, which is what switches wire-codec generation on in a
// compilation that references it. See RaskCqrsTransportAttribute for why the gate exists.
[assembly: RaskCqrsTransport]
