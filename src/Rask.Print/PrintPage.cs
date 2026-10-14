using System.Text.RegularExpressions;

namespace Rask.Printing;

/// <summary>
///     The paper a page prints on, written as the <c>@page</c> rule: <c>PrintPage.Size("A4").Landscape().Margins("15mm")</c>.
///     A value; every step hands back a new one.
/// </summary>
public readonly partial record struct PrintPage
{
    private readonly string? _size;
    private readonly bool _landscape;
    private readonly string? _margins;

    private PrintPage(string? size, bool landscape, string? margins)
    {
        _size = size;
        _landscape = landscape;
        _margins = margins;
    }

    /// <summary>A named paper (<c>A4</c>, <c>Letter</c>) or a size in CSS lengths (<c>210mm 297mm</c>).</summary>
    /// <exception cref="ArgumentException">The text is not a paper name or CSS lengths.</exception>
    public static PrintPage Size(string size) => new PrintPage().WithSize(size);

    /// <summary>The same paper, turned on its side.</summary>
    public PrintPage Landscape() => new(_size, true, _margins);

    /// <summary>The same paper, upright (the default).</summary>
    public PrintPage Portrait() => new(_size, false, _margins);

    /// <summary>The page's margins as CSS writes them: <c>15mm</c> or <c>20mm 15mm</c>.</summary>
    /// <exception cref="ArgumentException">The text is not CSS lengths.</exception>
    public PrintPage Margins(string margins) => new(_size, _landscape, Checked(margins, nameof(margins)));

    /// <summary>The <c>@page</c> rule, or nothing when no step was taken.</summary>
    internal string Rule()
    {
        var size = (_size, _landscape) switch
        {
            (null, false) => null,
            (null, true) => "landscape",
            (var named, false) => named,
            (var named, true) => named + " landscape",
        };
        var declarations = string.Concat(size is null ? "" : $"size:{size};", _margins is null ? "" : $"margin:{_margins};");
        return declarations.Length == 0 ? "" : $"@page{{{declarations}}}";
    }

    private PrintPage WithSize(string size) => new(Checked(size, nameof(size)), _landscape, _margins);

    // Only what names a paper or a length gets through: this text lands in a stylesheet.
    private static string Checked(string text, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A paper size or CSS length is needed.", parameter);
        }

        return Allowed().IsMatch(text)
            ? text.Trim()
            : throw new ArgumentException($"'{text}' is not a paper size or CSS length: letters, digits, '.', '%' and spaces only.", parameter);
    }

    [GeneratedRegex(@"^[A-Za-z0-9.% ]{1,40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Allowed();
}
