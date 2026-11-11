using System.IO.Compression;
using System.Xml;

namespace Rask.Workbooks;

/// <summary>
///     The zip a workbook is, opened to read: each part through an <see cref="XmlReader" /> that resolves nothing and
///     refuses a DTD, unpacked no further than its cap.
/// </summary>
internal sealed class Package : IDisposable
{
    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    public Package(byte[] file, ReadLimits limits)
    {
        Limits = limits;
        try
        {
            zip = new ZipArchive(new MemoryStream(file, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException e)
        {
            throw Malformed("it is not a zip archive", e);
        }

        foreach (var entry in zip.Entries)
        {
            entries.TryAdd(entry.FullName.TrimStart('/'), entry);
        }
    }

    public ReadLimits Limits { get; }

    public static InvalidDataException Malformed(string why, Exception? inner = null) =>
        new($"The file is not an .xlsx workbook Xlsx.Read can open: {why}.", inner);

    /// <summary>A relationship's target, as a part name: relative to the part it is from, or from the root when it starts with <c>/</c>.</summary>
    public static string Resolve(string from, string target)
    {
        var path = Uri.UnescapeDataString(target);
        var segments = new List<string>();
        if (!path.StartsWith('/'))
        {
            segments.AddRange(from.Split('/')[..^1]);
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment is not ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    public bool Has(string part) => entries.ContainsKey(part);

    /// <summary>Reads one part; XML that is not well-formed is said to be so, with the part it is in.</summary>
    public T Read<T>(string part, Func<XmlReader, T> read)
    {
        if (!entries.TryGetValue(part, out var entry))
        {
            throw Malformed($"it has no part '{part}'");
        }

        if (entry.Length > Limits.MaxPartBytes)
        {
            throw CappedStream.TooLarge(part, Limits.MaxPartBytes);
        }

        try
        {
            using var reader = XmlReader.Create(new CappedStream(entry.Open(), Limits.MaxPartBytes, part), Settings());
            return read(reader);
        }
        catch (XmlException e)
        {
            throw Malformed($"its part '{part}' is not well-formed XML ({e.Message})", e);
        }
    }

    public void Dispose() => zip.Dispose();

    // a DTD is refused outright, and nothing outside the file is ever fetched
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };
}
