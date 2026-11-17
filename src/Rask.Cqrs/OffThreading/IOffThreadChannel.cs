namespace Rask.Cqrs;

// The line between the page and its worker. Rask.Wasm draws it with postMessage; a test draws it in-process.
internal interface IOffThreadChannel
{
    // Sends a request frame and answers with the worker's frame. Starts the worker first when none is running. Faults
    // when the worker is stopped or dies before it answers.
    Task<byte[]> Call(int id, byte[] frame);

    // Asks the worker to cancel one request. A handler that never yields cannot hear it.
    void Cancel(int id);

    // Stops the worker, failing every call still waiting on it. The next call starts a new one.
    void Restart();

    // Starts the worker now rather than at the first call.
    void Warm();
}
