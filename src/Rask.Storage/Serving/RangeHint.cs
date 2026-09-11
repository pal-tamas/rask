using Microsoft.AspNetCore.Http;

namespace Rask.Storage.Serving;

/// <summary>
/// The single byte range a request asked for, resolved against the file's size — a hint that lets a remote store
/// fetch just those bytes. ASP.NET still decides whether the range is honoured; a wrong hint only costs a reopen.
/// </summary>
internal static class RangeHint
{
    internal static (long? From, long? To) Of(HttpRequest request, long size)
    {
        var header = request.GetTypedHeaders().Range;
        if (header is null || header.Ranges.Count != 1 || size <= 0)
        {
            return (null, null);
        }

        var range = header.Ranges.First();
        long from;
        long to;
        if (range.From is { } start)
        {
            from = start;
            to = Math.Min(range.To ?? size - 1, size - 1);
        }
        else if (range.To is { } suffix)
        {
            from = Math.Max(0, size - suffix);
            to = size - 1;
        }
        else
        {
            return (null, null);
        }

        return from <= to ? (from, to) : (null, null);
    }
}
