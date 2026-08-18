using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Forms;

internal static class FileListReader
{
    public static IReadOnlyList<RaskFile> Read(JsonElement payload, string property = "files")
    {
        if (!payload.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RaskFile>();
        }

        var backend = ResolveBackend();
        if (backend is null)
        {
            // The client sent files and the handler is about to be told there were none. Silence here is the
            // worst failure mode in the framework — the user picks a file, the UI reports success, and the
            // upload never happened — and it is exactly how the native host went a release without file
            // input. Every host registers a backend now, so this means the container was built by hand.
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Forms",
                $"[Rask.Forms] {arr.GetArrayLength()} file(s) arrived from the client but no "
                + "IBrowserFileBackend is registered, so the handler will receive an empty list. Every Rask "
                + "host registers one; if you built this container yourself, register a backend.");
            return Array.Empty<RaskFile>();
        }

        var list = new List<RaskFile>(arr.GetArrayLength());
        foreach (var meta in arr.EnumerateArray())
        {
            if (meta.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(backend.Create(meta));
        }

        return list;
    }

    public static IBrowserFileBackend? ResolveBackend()
    {
        var services = DispatchServicesScope.Current ?? LiveRenderContext.Current?.Services;
        return services?.GetService<IBrowserFileBackend>();
    }
}
