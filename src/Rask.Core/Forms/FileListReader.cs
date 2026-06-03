using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
