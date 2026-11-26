using System.Text.Json;

namespace Rask.Core.Tests.Dom;

/// <summary>The committed <c>src/Rask.Core/Dom/mdn.snapshot.json</c>, read and parsed once for every test that reads it.</summary>
internal static class MdnSnapshot
{
    public static string Text { get; } = Load();

    public static JsonElement Root { get; } = JsonDocument.Parse(Text).RootElement;

    private static string Load()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return File.ReadAllText(Path.Combine(dir, "src", "Rask.Core", "Dom", "mdn.snapshot.json"));
            }
        }

        throw new InvalidOperationException("Rask.slnx not found above the test output.");
    }
}
