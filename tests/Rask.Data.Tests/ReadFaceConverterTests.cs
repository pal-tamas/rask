using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data.Tests;

/// <summary>A file held as a value object of several values, its bytes compressed by the write model.</summary>
public sealed record CofferAttachment(string FileName, byte[] Content);

/// <summary>A value object of one value, which the write model seals through a converter of its own.</summary>
public sealed record CofferCode(string Value);

/// <summary>How a coffer is kept, stored as its name.</summary>
public enum CofferKind
{
    Open,
    Locked,
}

/// <summary>
/// An aggregate whose write model stores values through converters that keep the type: bytes compressed to bytes,
/// a string sealed to a string.
/// </summary>
public sealed class Coffer : Aggregate<int>
{
    private Coffer() { }

    public string FileName { get; private set; } = "";

    public byte[] Content { get; private set; } = [];

    public string Secret { get; private set; } = "";

    public CofferAttachment Attachment { get; private set; } = new("", []);

    public CofferCode Code { get; private set; } = new("");

    public CofferKind Kind { get; private set; }

    public static Coffer Holding(string fileName, byte[] content, string secret, string code = "C-0", CofferKind kind = CofferKind.Open) =>
        new() { FileName = fileName, Content = content, Secret = secret, Attachment = new(fileName, content), Code = new(code), Kind = kind };

    public static void Configure(EntityTypeBuilder<Coffer> builder)
    {
        builder.ToTable("Coffers");
        builder.Property(v => v.Content).HasConversion(CofferConverters.Compressed);
        builder.Property(v => v.Secret).HasConversion(CofferConverters.Sealed);
        builder.ComplexProperty(v => v.Attachment).Property(a => a.Content).HasConversion(CofferConverters.Compressed);
        builder.Property(v => v.Code).HasConversion(CofferConverters.SealedCode);
        builder.Property(v => v.Kind).HasConversion<string>();
    }
}

/// <summary>Converters that keep the CLR type: what the column holds is not what the model holds.</summary>
public static class CofferConverters
{
    public const string SealPrefix = "sealed:";

    public static readonly ValueConverter<byte[], byte[]> Compressed = new(v => Compress(v), v => Decompress(v));

    public static readonly ValueConverter<string, string> Sealed = new(v => Seal(v), v => Unseal(v));

    public static readonly ValueConverter<CofferCode, string> SealedCode = new(v => Seal(v.Value), v => new CofferCode(Unseal(v)));

    public static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(value);
        }

        return output.ToArray();
    }

    public static byte[] Decompress(byte[] value)
    {
        using var input = new GZipStream(new MemoryStream(value), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    public static string Seal(string value) => SealPrefix + new string([.. value.Reverse()]);

    public static string Unseal(string value) => new([.. value[SealPrefix.Length..].Reverse()]);
}

/// <summary>
/// The read face reads a column through every converter the write model put on it — including one that keeps
/// the type, which a face holding the stored type used to skip, handing back the compressed or sealed value.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ReadFaceConverterTests : IDisposable
{
    private static readonly byte[] Original = [.. Enumerable.Repeat((byte)'r', 512), 1, 2, 3];

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-read-converters-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Bytes_compressed_to_bytes_are_read_back_decompressed_through_where_select_and_first()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Coffer.Holding("report.pdf", Original, "pin"));

        var file = await Coffer.Where(v => v.Id == id).Select(v => new { v.FileName, v.Content }).First(TestContext.Current.CancellationToken);

        Assert.Equal("report.pdf", file!.FileName);
        Assert.Equal(Original, file.Content);
    }

    [Fact]
    public async Task A_string_sealed_to_a_string_is_read_back_unsealed_and_found_by_its_plain_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Coffer.Holding("a.txt", Original, "open sesame"));

        var secrets = await Coffer.Where(v => v.Secret == "open sesame").Select(v => v.Secret);

        Assert.Equal(["open sesame"], secrets);
    }

    [Fact]
    public async Task Bytes_compressed_inside_a_value_object_are_read_back_decompressed()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Coffer.Holding("inner.bin", Original, "pin"));

        var content = await Coffer.Where(v => v.Id == id).Select(v => v.AttachmentContent).First(TestContext.Current.CancellationToken);

        Assert.Equal(Original, content);
    }

    [Fact]
    public async Task A_one_value_value_object_sealed_by_its_own_converter_is_read_back_unsealed()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Coffer.Holding("a.txt", Original, "pin", code: "AB-1"));
        await SaveAsync(Coffer.Holding("b.txt", Original, "pin", code: "CD-2"));

        var codes = await Coffer.Where(v => v.Code == "AB-1").Select(v => v.Code);

        Assert.Equal(["AB-1"], codes);
    }

    [Fact]
    public async Task An_enum_stored_as_its_name_is_read_back_and_found_as_the_enum()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Coffer.Holding("a.txt", Original, "pin", kind: CofferKind.Locked));
        await SaveAsync(Coffer.Holding("b.txt", Original, "pin"));

        var locked = await Coffer.Where(v => v.Kind == CofferKind.Locked).Select(v => v.FileName);

        Assert.Equal(["a.txt"], locked);
    }

    [Fact]
    public async Task The_write_side_still_stores_the_converted_value_and_loads_the_original()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Coffer.Holding("w.bin", Original, "pin"));

        var stored = (await database.Load<Coffer>(id, TestContext.Current.CancellationToken))!;
        var raw = await database.Context.Database
            .SqlQuery<string>($"SELECT Secret AS Value FROM Coffers WHERE Id = {id}")
            .SingleAsync(TestContext.Current.CancellationToken);
        var rawContent = await database.Context.Database
            .SqlQuery<byte[]>($"SELECT Content AS Value FROM Coffers WHERE Id = {id}")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Original, stored.Content);
        Assert.Equal(Original, stored.Attachment.Content);
        Assert.Equal("pin", stored.Secret);
        Assert.Equal(new CofferCode("C-0"), stored.Code);
        Assert.Equal(CofferConverters.Seal("pin"), raw);
        Assert.Equal(Original, CofferConverters.Decompress(rawContent));
    }

    private static async Task<int> SaveAsync(Coffer coffer)
    {
        await coffer.Save(cancellationToken: TestContext.Current.CancellationToken);
        return coffer.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
