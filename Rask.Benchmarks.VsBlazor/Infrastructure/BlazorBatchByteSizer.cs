using System.Reflection;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Serializes a <see cref="RenderBatch"/> to its on-the-wire byte sequence using
///     the same internal <c>RenderBatchWriter</c> that Blazor Server's SignalR circuit
///     uses, and returns the byte count. We reach the internal type via reflection
///     because Blazor doesn't expose it; the cost of reflection is borne once at
///     setup (delegate cached) so per-iteration overhead is one virtual call plus the
///     real serializer body.
///     <para>
///         If the type ever moves or is removed, <see cref="Measure"/> throws
///         <see cref="InvalidOperationException"/> with a hint to switch to the
///         handwritten fallback. We pin the framework version against which the suite
///         was validated in <c>Baselines/vs-blazor.md</c>.
///     </para>
/// </summary>
internal static class BlazorBatchByteSizer
{
    private static readonly Lazy<RenderBatchWriterAdapter> Adapter = new(BuildAdapter);

    public static long Measure(in RenderBatch batch)
    {
        return Adapter.Value.Measure(in batch);
    }

    private static RenderBatchWriterAdapter BuildAdapter()
    {
        // RenderBatchWriter lives in Microsoft.AspNetCore.Components.Server. Find it via
        // a known public type from the same assembly so we don't depend on Assembly.Load.
        var serverAssembly = typeof(Microsoft.AspNetCore.Components.Server.ServerComponentsEndpointOptions).Assembly;
        var writerType = serverAssembly.GetType(
                             "Microsoft.AspNetCore.Components.Server.Circuits.RenderBatchWriter")
                         ?? throw new InvalidOperationException(
                             "RenderBatchWriter not found. The internal Blazor API has likely shifted; " +
                             "fall back to the handwritten serializer (see vs-blazor.md methodology).");

        var allCtors = writerType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var allMethods = writerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Write")
            .ToArray();

        var ctor = writerType.GetConstructor(
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                       binder: null,
                       types: [typeof(Stream), typeof(bool)],
                       modifiers: null)
                   ?? throw new InvalidOperationException(
                       "RenderBatchWriter(Stream, bool) ctor not found. Available ctors: " +
                       string.Join(" | ", allCtors.Select(c => c.ToString())));

        var writeMethod = writerType.GetMethod(
                              "Write",
                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                              binder: null,
                              types: [typeof(RenderBatch).MakeByRefType()],
                              modifiers: null)
                          ?? throw new InvalidOperationException(
                              "RenderBatchWriter.Write(in RenderBatch) not found. Available Write methods: " +
                              string.Join(" | ", allMethods.Select(m => m.ToString())));

        return new RenderBatchWriterAdapter(ctor, writeMethod);
    }

    /// <summary>
    ///     Cached reflection handles plus a reusable MemoryStream. Single instance reused
    ///     across all benchmark iterations — RenderBatchWriter is stateful per-instance
    ///     (it builds a string table during Write), so we rebuild the writer per call.
    /// </summary>
    private sealed class RenderBatchWriterAdapter
    {
        private readonly ConstructorInfo _ctor;
        private readonly MethodInfo _write;
        private readonly MemoryStream _stream = new(capacity: 8 * 1024);
        private readonly object[] _writeArgs = new object[1];

        public RenderBatchWriterAdapter(ConstructorInfo ctor, MethodInfo write)
        {
            _ctor = ctor;
            _write = write;
        }

        public long Measure(in RenderBatch batch)
        {
            _stream.SetLength(0);
            _stream.Position = 0;

            // ctor: RenderBatchWriter(Stream, bool leaveOpen=true)
            var writer = _ctor.Invoke([_stream, true]);
            try
            {
                // The Write method takes `in RenderBatch`. Reflection.Invoke unboxes the
                // single object argument and passes a managed pointer (the same ABI as a
                // ref/in parameter for value types). Boxing here is one alloc per call;
                // measurements stay deterministic because the cost is constant.
                _writeArgs[0] = batch;
                _write.Invoke(writer, _writeArgs);
                _writeArgs[0] = null!;
            }
            finally
            {
                (writer as IDisposable)?.Dispose();
            }

            return _stream.Length;
        }
    }
}
