using System.Text.Json;

namespace Rask.Core.Tests.Dom;

/// <summary>The committed <c>src/Rask.Core/Dom/mdn.snapshot.json</c>, parsed once for every test that reads it.</summary>
internal static class MdnSnapshot
{
    public static JsonElement Root { get; } = Load();

    private static JsonElement Load()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "src", "Rask.Core", "Dom", "mdn.snapshot.json"))).RootElement;
            }
        }

        throw new InvalidOperationException("Rask.slnx not found above the test output.");
    }
}
